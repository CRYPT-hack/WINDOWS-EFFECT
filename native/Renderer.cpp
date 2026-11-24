#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <d3d11.h>
#include <dxgi1_3.h>
#include <dcomp.h>
#include <wrl/client.h>
#include <memory>
#include <new>
#include "Vertex.h"
#include "Pixel.h"

using Microsoft::WRL::ComPtr;
#define API extern "C" __declspec(dllexport)
#define TRY(call) do { HRESULT result = (call); if (FAILED(result)) return result; } while (false)

struct Mark { float center[2], halfSize[2], axis[2]; UINT color, shape, depth; };
struct RectF { float left, top, right, bottom; };
struct View { float width, height, scale; UINT windows; };
static_assert(sizeof(Mark)==36, "Managed mark layout must match the shader");

struct Upload {
    ComPtr<ID3D11Buffer> buffer;
    ComPtr<ID3D11ShaderResourceView> srv;
    UINT capacity=0;
    HRESULT Write(ID3D11Device* device, ID3D11DeviceContext* ctx, const void* src, UINT count, UINT stride) {
        if(!buffer || capacity<count) {
            UINT next=64;
            while(next<count) next*=2;
            D3D11_BUFFER_DESC desc={};
            desc.ByteWidth=next*stride;
            desc.Usage=D3D11_USAGE_DYNAMIC;
            desc.BindFlags=D3D11_BIND_SHADER_RESOURCE;
            desc.CPUAccessFlags=D3D11_CPU_ACCESS_WRITE;
            desc.MiscFlags=D3D11_RESOURCE_MISC_BUFFER_STRUCTURED;
            desc.StructureByteStride=stride;
            TRY(device->CreateBuffer(&desc,nullptr,buffer.ReleaseAndGetAddressOf()));
            TRY(device->CreateShaderResourceView(buffer.Get(),nullptr,srv.ReleaseAndGetAddressOf()));
            capacity=next;
        }
        if(count) {
            D3D11_MAPPED_SUBRESOURCE map;
            TRY(ctx->Map(buffer.Get(),0,D3D11_MAP_WRITE_DISCARD,0,&map));
            memcpy(map.pData,src,count*stride);
            ctx->Unmap(buffer.Get(),0);
        }
        return S_OK;
    }
};

static LRESULT CALLBACK OverlayProc(HWND window,UINT msg,WPARAM w,LPARAM l) {
    if(msg==WM_NCHITTEST) return HTTRANSPARENT;
    if(msg==WM_MOUSEACTIVATE) return MA_NOACTIVATE;
    if(msg==WM_ERASEBKGND) return 1;
    return DefWindowProcW(window,msg,w,l);
}

struct Renderer {
    HWND hwnd=nullptr;
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> ctx;
    ComPtr<IDXGISwapChain1> chain;
    ComPtr<ID3D11RenderTargetView> target;
    ComPtr<IDCompositionDevice> composition;
    ComPtr<IDCompositionTarget> destination;
    ComPtr<IDCompositionVisual> visual;
    ComPtr<ID3D11VertexShader> vertex;
    ComPtr<ID3D11PixelShader> pixel;
    ComPtr<ID3D11BlendState> blend;
    ComPtr<ID3D11RasterizerState> raster;
    ComPtr<ID3D11Buffer> view;
    Upload marks, windows;
    UINT rectCount=0;
    ~Renderer() { if(hwnd) DestroyWindow(hwnd); }
    HRESULT Init(int x,int y,int w,int h) {
        WNDCLASSW wc={};
        wc.hInstance=GetModuleHandleW(nullptr); wc.lpfnWndProc=OverlayProc; wc.lpszClassName=L"WindowWeather.Compositor";
        if(!RegisterClassW(&wc) && GetLastError()!=ERROR_CLASS_ALREADY_EXISTS) return HRESULT_FROM_WIN32(GetLastError());
        hwnd=CreateWindowExW(WS_EX_TOPMOST|WS_EX_TRANSPARENT|WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW|WS_EX_LAYERED|WS_EX_NOREDIRECTIONBITMAP,
            wc.lpszClassName,L"Window Weather overlay",WS_POPUP,x,y,w,h,nullptr,nullptr,wc.hInstance,nullptr);
        if(!hwnd) return HRESULT_FROM_WIN32(GetLastError());
        if(!SetLayeredWindowAttributes(hwnd,0,255,LWA_ALPHA)) return HRESULT_FROM_WIN32(GetLastError());
        D3D_FEATURE_LEVEL wanted=D3D_FEATURE_LEVEL_11_0;
        TRY(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            &wanted,1,D3D11_SDK_VERSION,device.GetAddressOf(),nullptr,ctx.GetAddressOf()));
        ComPtr<IDXGIDevice1> dx;
        TRY(device.As(&dx));
        TRY(dx->SetMaximumFrameLatency(1));
        ComPtr<IDXGIAdapter> adapter;
        TRY(dx->GetAdapter(adapter.GetAddressOf()));
        ComPtr<IDXGIFactory2> factory;
        TRY(adapter->GetParent(IID_PPV_ARGS(factory.GetAddressOf())));
        DXGI_SWAP_CHAIN_DESC1 sd={};
        sd.Width=w; sd.Height=h; sd.Format=DXGI_FORMAT_B8G8R8A8_UNORM;
        sd.SampleDesc.Count=1; sd.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT;
        sd.BufferCount=2; sd.SwapEffect=DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL;
        sd.AlphaMode=DXGI_ALPHA_MODE_PREMULTIPLIED; sd.Scaling=DXGI_SCALING_STRETCH;
        TRY(factory->CreateSwapChainForComposition(device.Get(),&sd,nullptr,chain.GetAddressOf()));
        ComPtr<ID3D11Texture2D> back;
        TRY(chain->GetBuffer(0,IID_PPV_ARGS(back.GetAddressOf())));
        TRY(device->CreateRenderTargetView(back.Get(),nullptr,target.GetAddressOf()));
        TRY(DCompositionCreateDevice(dx.Get(),IID_PPV_ARGS(composition.GetAddressOf())));
        TRY(composition->CreateTargetForHwnd(hwnd,TRUE,destination.GetAddressOf()));
        TRY(composition->CreateVisual(visual.GetAddressOf()));
        TRY(visual->SetContent(chain.Get()));
        TRY(destination->SetRoot(visual.Get()));
        TRY(composition->Commit());
        TRY(device->CreateVertexShader(g_Vertex,sizeof(g_Vertex),nullptr,vertex.GetAddressOf()));
        TRY(device->CreatePixelShader(g_Pixel,sizeof(g_Pixel),nullptr,pixel.GetAddressOf()));
        D3D11_BUFFER_DESC cb={}; cb.ByteWidth=sizeof(View); cb.Usage=D3D11_USAGE_DEFAULT; cb.BindFlags=D3D11_BIND_CONSTANT_BUFFER;
        TRY(device->CreateBuffer(&cb,nullptr,view.GetAddressOf()));
        D3D11_BLEND_DESC bd={};
        auto& b=bd.RenderTarget[0]; b.BlendEnable=TRUE; b.SrcBlend=b.SrcBlendAlpha=D3D11_BLEND_ONE;
        b.DestBlend=b.DestBlendAlpha=D3D11_BLEND_INV_SRC_ALPHA; b.BlendOp=b.BlendOpAlpha=D3D11_BLEND_OP_ADD;
        b.RenderTargetWriteMask=D3D11_COLOR_WRITE_ENABLE_ALL;
        TRY(device->CreateBlendState(&bd,blend.GetAddressOf()));
        D3D11_RASTERIZER_DESC rd={}; rd.FillMode=D3D11_FILL_SOLID; rd.CullMode=D3D11_CULL_NONE; rd.DepthClipEnable=TRUE;
        TRY(device->CreateRasterizerState(&rd,raster.GetAddressOf()));
        D3D11_VIEWPORT vp={0,0,static_cast<float>(w),static_cast<float>(h),0,1};
        ctx->RSSetViewports(1,&vp); ctx->RSSetState(raster.Get());
        ctx->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLESTRIP);
        ctx->VSSetShader(vertex.Get(),nullptr,0); ctx->PSSetShader(pixel.Get(),nullptr,0);
        ctx->VSSetConstantBuffers(0,1,view.GetAddressOf()); ctx->PSSetConstantBuffers(0,1,view.GetAddressOf());
        ctx->OMSetBlendState(blend.Get(),nullptr,0xffffffff);
        return S_OK;
    }
    HRESULT Frame(const Mark* items,UINT count,float width,float height,float scale) {
        TRY(marks.Write(device.Get(),ctx.Get(),items,count,sizeof(Mark)));
        View v={width,height,scale,rectCount};
        ctx->UpdateSubresource(view.Get(),0,nullptr,&v,0,0);
        ctx->VSSetShaderResources(0,1,marks.srv.GetAddressOf());
        ctx->PSSetShaderResources(1,1,windows.srv.GetAddressOf());
        ctx->OMSetRenderTargets(1,target.GetAddressOf(),nullptr);
        const float clear[4]={};
        ctx->ClearRenderTargetView(target.Get(),clear);
        ctx->DrawInstanced(4,count,0,0);
        // A busy compositor can skip a frame without blocking the desktop UI thread.
        HRESULT hr=chain->Present(0,DXGI_PRESENT_DO_NOT_WAIT);
        return hr==DXGI_ERROR_WAS_STILL_DRAWING ? S_FALSE : hr;
    }
};

API HRESULT WeatherCreate(int x,int y,int w,int h,Renderer** renderer,HWND* hwnd) {
    std::unique_ptr<Renderer> r(new(std::nothrow) Renderer());
    if(!r) return E_OUTOFMEMORY;
    TRY(r->Init(x,y,w,h));
    *hwnd=r->hwnd; *renderer=r.release(); return S_OK;
}
API HRESULT WeatherWindows(Renderer* r,const RectF* rects,UINT n) {
    TRY(r->windows.Write(r->device.Get(),r->ctx.Get(),rects,n,sizeof(RectF)));
    r->rectCount=n; return S_OK;
}
API HRESULT WeatherFrame(Renderer* r,const Mark* marks,UINT n,float w,float h,float scale) {
    return r->Frame(marks,n,w,h,scale);
}
API void WeatherDestroy(Renderer* r) { delete r; }
