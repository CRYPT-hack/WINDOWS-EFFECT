#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shlobj.h>
#include <exdisp.h>
#include <wrl/client.h>
#include <cstdint>

using Microsoft::WRL::ComPtr;
struct IconLedge { uint64_t key; RECT bounds; };

// The caller runs this on a COM worker. Only geometry and an opaque item key
// cross the native boundary. Positions are in physical virtual-desktop pixels.
extern "C" __declspec(dllexport) HRESULT WeatherDesktopIcons(IconLedge* icons,int capacity,int* count) {
    struct Apartment {
        HRESULT status=CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED);
        ~Apartment(){if(SUCCEEDED(status))CoUninitialize();}
    } apartment;
    if(FAILED(apartment.status) && apartment.status!=RPC_E_CHANGED_MODE)return apartment.status;
    *count=0;
    ComPtr<IShellWindows> shell;
    HRESULT hr=CoCreateInstance(CLSID_ShellWindows,nullptr,CLSCTX_LOCAL_SERVER,IID_PPV_ARGS(&shell));
    if(FAILED(hr))return hr;
    VARIANT desktop={},unused={};
    desktop.vt=VT_I4; desktop.lVal=CSIDL_DESKTOP;
    long window=0;
    ComPtr<IDispatch> dispatch;
    hr=shell->FindWindowSW(&desktop,&unused,SWC_DESKTOP,&window,SWFO_NEEDDISPATCH,&dispatch);
    if(hr!=S_OK || !dispatch)return S_FALSE;
    ComPtr<IServiceProvider> services;
    ComPtr<IShellBrowser> browser;
    ComPtr<IShellView> view;
    ComPtr<IFolderView2> folder;
    if(FAILED(hr=dispatch.As(&services)) ||
       FAILED(hr=services->QueryService(SID_STopLevelBrowser,IID_PPV_ARGS(&browser))) ||
       FAILED(hr=browser->QueryActiveShellView(&view)) || FAILED(hr=view.As(&folder)))return hr;
    HWND host=nullptr;
    if(FAILED(hr=view->GetWindow(&host)))return hr;
    HWND list=FindWindowExW(host,nullptr,L"SysListView32",nullptr);
    DWORD flags=0;
    if(!IsWindowVisible(list) || (SUCCEEDED(folder->GetCurrentFolderFlags(&flags)) && (flags&FWF_NOICONS)))return S_OK;
    int entries=0,iconSize=0;
    FOLDERVIEWMODE mode;
    if(FAILED(hr=folder->ItemCount(SVGIO_ALLVIEW,&entries)) ||
       FAILED(hr=folder->GetViewModeAndIconSize(&mode,&iconSize)))return hr;
    if(entries>capacity){*count=entries;return HRESULT_FROM_WIN32(ERROR_INSUFFICIENT_BUFFER);}
    POINT origin={};
    if(!ClientToScreen(list,&origin) || iconSize<=0)return E_FAIL;
    for(int i=0;i<entries;i++){
        PITEMID_CHILD item=nullptr;
        if(FAILED(folder->Item(i,&item)) || !item)continue;
        POINT pos={};
        if(SUCCEEDED(folder->GetItemPosition(item,&pos))) {
            const BYTE* data=reinterpret_cast<const BYTE*>(item);
            uint64_t hash=14695981039346656037ULL;
            for(UINT k=0,n=ILGetSize(item);k<n;k++)hash=(hash^data[k])*1099511628211ULL;
            int x=origin.x+pos.x,y=origin.y+pos.y;
            icons[*count]={hash,{x,y,x+iconSize,y+iconSize}};
            ++*count;
        }
        CoTaskMemFree(item);
    }
    return S_OK;
}
