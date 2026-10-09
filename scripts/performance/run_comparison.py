"""Run bounded autoplay comparisons in an isolated runtime; requires --visible."""
from __future__ import annotations
import argparse
import csv
import ctypes as c
from ctypes import wintypes as w
from datetime import datetime
import hashlib
import os
from pathlib import Path
import shutil
import struct
import subprocess
import time
import tomllib
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]

class Message(c.Structure):
    _fields_ = [('hwnd', w.HWND), ('message', w.UINT), ('wParam', w.WPARAM), ('lParam', w.LPARAM), ('time', w.DWORD), ('pt', w.POINT), ('private', w.DWORD)]


class ProcessEntry(c.Structure):
    _fields_ = [('dwSize',w.DWORD),('cntUsage',w.DWORD),('pid',w.DWORD),('heap',c.c_size_t),
                ('module',w.DWORD),('threads',w.DWORD),('parent',w.DWORD),('priority',w.LONG),
                ('flags',w.DWORD),('exe',w.WCHAR*260)]


def process_entries():
    k=c.WinDLL('kernel32',use_last_error=True)
    k.CreateToolhelp32Snapshot.argtypes=[w.DWORD,w.DWORD];k.CreateToolhelp32Snapshot.restype=w.HANDLE
    k.Process32FirstW.argtypes=[w.HANDLE,c.POINTER(ProcessEntry)]
    k.Process32NextW.argtypes=[w.HANDLE,c.POINTER(ProcessEntry)]
    k.CloseHandle.argtypes=[w.HANDLE]
    handle=k.CreateToolhelp32Snapshot(2,0)
    if handle==c.c_void_p(-1).value:raise c.WinError(c.get_last_error())
    try:
        entry=ProcessEntry();entry.dwSize=c.sizeof(entry)
        available=k.Process32FirstW(handle,c.byref(entry));active=[]
        while available:
            active.append((entry.exe,entry.pid,entry.parent))
            available=k.Process32NextW(handle,c.byref(entry))
        return active
    finally:k.CloseHandle(handle)


def ensure_idle():
    active=[(exe,pid) for exe,pid,parent in process_entries() if exe.lower() in {'lazerrave.exe','openlr2_x64.exe','openlr2_x86.exe'}]
    if active:raise RuntimeError(f'Close existing LazerRave/OpenLR2 instances before testing: {active}')


class LiveFrames:
    fields=['engine_time_ms','play_time_ms','fps','quarter_second_max_ms','combo','judged_notes','total_notes',
            'scene','phase','playing','completed','bpm','chart_time','render_time','dropped_rows','song_duration_ms','recorded_frames']
    def __init__(self,pid):
        self.pid=pid;self.handle=None;self.view=None
        self.api=c.WinDLL('kernel32',use_last_error=True)
        self.api.OpenFileMappingW.argtypes=[w.DWORD,w.BOOL,w.LPCWSTR];self.api.OpenFileMappingW.restype=w.HANDLE
        self.api.MapViewOfFile.argtypes=[w.HANDLE,w.DWORD,w.DWORD,w.DWORD,c.c_size_t];self.api.MapViewOfFile.restype=c.c_void_p
        self.api.UnmapViewOfFile.argtypes=[c.c_void_p];self.api.CloseHandle.argtypes=[w.HANDLE]
        self.handle=self.api.OpenFileMappingW(4,False,f'Local\\LazerRave-frame-trace-{pid}')
        if self.handle:self.view=self.api.MapViewOfFile(self.handle,4,0,0,144)
    def read(self):
        if not self.view:return None
        for _ in range(3):
            version,sequence,*values=struct.unpack('<II17d',c.string_at(self.view,144))
            after=c.c_uint32.from_address(self.view+4).value
            if version==1 and sequence==after and sequence>0 and not sequence%2:
                return dict(zip(self.fields,values))
        return None
    def close(self):
        if self.view:self.api.UnmapViewOfFile(self.view);self.view=None
        if self.handle:self.api.CloseHandle(self.handle);self.handle=None


def window_api():
    u = c.WinDLL('user32', use_last_error=True)
    u.CreateWindowExW.argtypes = [w.DWORD,w.LPCWSTR,w.LPCWSTR,w.DWORD,c.c_int,c.c_int,c.c_int,c.c_int,w.HWND,w.HMENU,w.HINSTANCE,c.c_void_p]
    u.CreateWindowExW.restype = w.HWND
    u.PeekMessageW.argtypes = [c.POINTER(Message),w.HWND,w.UINT,w.UINT,w.UINT]
    u.TranslateMessage.argtypes = [c.POINTER(Message)]
    u.DispatchMessageW.argtypes = [c.POINTER(Message)]
    u.DispatchMessageW.restype = w.LPARAM
    u.DestroyWindow.argtypes = [w.HWND]
    u.IsWindow.argtypes = [w.HWND]
    u.SetForegroundWindow.argtypes = [w.HWND]
    u.SetFocus.argtypes = [w.HWND]
    u.PostMessageW.argtypes = [w.HWND,w.UINT,w.WPARAM,w.LPARAM]
    u.GetWindowThreadProcessId.argtypes = [w.HWND,c.POINTER(w.DWORD)]
    return u


def pump(u):
    m=Message()
    while u.PeekMessageW(c.byref(m),None,0,0,1):
        u.TranslateMessage(c.byref(m));u.DispatchMessageW(c.byref(m))


def close_process_window(u,pid):
    callback_type = c.WINFUNCTYPE(w.BOOL,w.HWND,w.LPARAM)
    def close(hwnd,_):
        owner=w.DWORD();u.GetWindowThreadProcessId(hwnd,c.byref(owner))
        if owner.value==pid:u.PostMessageW(hwnd,0x10,0,0)
        return True
    callback=callback_type(close)
    u.EnumWindows.argtypes=[callback_type,w.LPARAM]
    u.EnumWindows(callback,0)


def digest_protected(source):
    paths=list((source/'userdata').rglob('*')) if (source/'userdata').exists() else []
    for sub in ['Config','Database/Score','Replay','Ghost']:
        folder=source/'LR2files'/sub
        if folder.exists():paths.extend(p for p in folder.rglob('*') if p.is_file())
    return {str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in paths if p.is_file()}


def prepare(source, runtime):
    shutil.copytree(source,runtime,ignore=shutil.ignore_patterns('BMS','logs','screenshot','cache'),dirs_exist_ok=True)
    binary=ROOT/'out/build/client/bin/Release/net10.0-windows/win-x64'
    shutil.copytree(binary,runtime,dirs_exist_ok=True)
    shutil.copy2(ROOT/'out/build/engine/windows-vs-x64/RelWithDebInfo/OpenLR2_x64.exe',runtime)

def configure_window(runtime, sync, size):
    ini=runtime/'userdata/framework.ini'
    ini.parent.mkdir(parents=True,exist_ok=True)
    lines=ini.read_text(encoding='utf-8-sig').splitlines() if ini.exists() else []
    values={'FrameSync':sync,'WindowMode':'Windowed','WindowedSize':size,'WindowedPositionX':'0.5','WindowedPositionY':'0.5'}
    lines=[line for line in lines if line.partition('=')[0].strip() not in values]
    lines.extend(f'{k} = {v}' for k,v in values.items())
    ini.write_text('\n'.join(lines)+'\n',encoding='utf-8')


def run_case(runtime, chart, mode, profile, seconds, frame_limit, output, full_song=False, watchdog=600):
    u=window_api();parent=child=None;process=None;forced=False
    request=output/'request.xml';live=None;timed_out=False
    env=os.environ.copy();env['LAZERRAVE_FRAME_TRACE']='1';env['LAZERRAVE_DIAGNOSTIC_AUTOPLAY']='1'
    if full_song:env['LAZERRAVE_DIAGNOSTIC_FULL_SONG']='1';env['LAZERRAVE_TRACE_CAPACITY']='1200000'
    existing=set((runtime/'logs').glob('engine-*')) if (runtime/'logs').exists() else set()
    try:
        if mode.startswith('frontend'):
            command=[str(runtime/'LazerRave.exe'),'--benchmark',str(chart),str(0 if full_song else seconds),profile,str(frame_limit), 'standalone' if mode=='frontend-standalone' else 'embedded']
        else:
            xml=ET.Element('lazerrave',version='1',mode='play')
            for key,value in [('chart',chart),('speed',200),('encoding','auto')]:ET.SubElement(xml,key).text=str(value)
            if mode=='host':
                parent=u.CreateWindowExW(0,'STATIC',f'LazerRave performance: {profile}',0x12CF0000,80,80,1280,800,None,None,None,None)
                if not parent:raise c.WinError(c.get_last_error())
                child=u.CreateWindowExW(0,'STATIC','OpenLR2 viewport',0x56000004,128,16,1024,768,parent,None,None,None)
                if not child:raise c.WinError(c.get_last_error())
                u.SetForegroundWindow(parent);u.SetFocus(child)
                for key,value in [('embed-window',child),('host-process',os.getpid()),('render-profile',profile),('frame-limit',frame_limit)]:ET.SubElement(xml,key).text=str(value)
            ET.ElementTree(xml).write(request,encoding='utf-8')
            command=[str(runtime/'OpenLR2_x64.exe'),'--lazerrave-request',str(request)]
        (runtime/'benchmark-result.txt').unlink(missing_ok=True)
        (runtime/'benchmark-error.txt').unlink(missing_ok=True)
        with (output/'process.log').open('wb') as log:
            process=subprocess.Popen(command,cwd=runtime,env=env,stdout=log,stderr=log,creationflags=subprocess.CREATE_NO_WINDOW)
            start=time.monotonic();deadline=start+(watchdog if full_song else seconds+(90 if mode.startswith('frontend') else 0))
            next_sample=start;next_print=start
            with (output/'live-fps.csv').open('w',newline='',encoding='utf-8') as monitor:
                writer=csv.DictWriter(monitor,fieldnames=['elapsed_s','engine_pid',*LiveFrames.fields]);writer.writeheader()
                while process.poll() is None and time.monotonic()<deadline:
                    now=time.monotonic()
                    if now>=next_sample:
                        next_sample=now+1
                        if not live or not live.view:
                            if live:live.close()
                            if mode.startswith('frontend'):
                                engines=[pid for exe,pid,parent in process_entries() if exe.lower()=='openlr2_x64.exe' and parent==process.pid]
                                live=LiveFrames(engines[0]) if engines else None
                            else:live=LiveFrames(process.pid)
                        snapshot=live.read() if live else None
                        if snapshot:
                            writer.writerow({'elapsed_s':f'{now-start:.3f}','engine_pid':live.pid,**snapshot});monitor.flush()
                            if now>=next_print:
                                next_print=now+10
                                print(f"  play={snapshot['play_time_ms']/1000:.1f}s / {snapshot['song_duration_ms']/1000:.1f}s, combo={snapshot['combo']:.0f}, FPS={snapshot['fps']:.1f}, notes={snapshot['judged_notes']:.0f}/{snapshot['total_notes']:.0f}",flush=True)
                    pump(u);time.sleep(.01)
            timed_out=full_song and process.poll() is None
            if process.poll() is None:
                Path(str(request)+'.stop').write_text('stop')
                close_process_window(u,process.pid)
                until=time.monotonic()+8
                while process.poll() is None and time.monotonic()<until:pump(u);time.sleep(.02)
            if process.poll() is None:forced=True;process.kill();process.wait(timeout=10)
        for p in (runtime/'logs').glob('engine-*'):
            if p not in existing:shutil.copy2(p,output)
        for name in ['benchmark-result.txt','benchmark-error.txt','frontend-fps.csv']:
            p=runtime/name
            if p.exists():shutil.copy2(p,output)
        traces=list(output.glob('engine-trace-*.csv'))
        print(f'{mode}/{profile}: exit={process.returncode}, forced={forced}, traces={len(traces)}, elapsed={time.monotonic()-start:.1f}s',flush=True)
        if timed_out:raise RuntimeError('Full-song watchdog expired; this is an incomplete recording')
        if forced or process.returncode!=0 or not traces:raise RuntimeError(f'Incomplete case: {output}')
        if full_song:
            sessions=list(output.glob('engine-session-*.csv'))
            if len(sessions)!=1:raise RuntimeError('Missing completion certificate')
            with sessions[0].open(newline='') as f:certificate=next(csv.DictReader(f))
            if certificate['completed']!='1' or certificate['dropped_rows']!='0' or certificate['total_notes']!=certificate['judged_notes']:
                raise RuntimeError(f'Incomplete full-song recording: {certificate}')
            print(f"Full song verified: {certificate['judged_notes']} notes; max combo {certificate['max_combo']}; zero dropped trace rows.",flush=True)
        if mode.startswith('frontend') and not (output/'benchmark-result.txt').read_text().startswith('PASS'):raise RuntimeError('Frontend session failed')
    finally:
        if live:live.close()
        if process and process.poll() is None:
            close_process_window(u,process.pid)
            try:process.wait(timeout=5)
            except subprocess.TimeoutExpired:process.kill();process.wait()
        if child and u.IsWindow(child):u.DestroyWindow(child)
        if parent and u.IsWindow(parent):u.DestroyWindow(parent)


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--visible',action='store_true',required=True)
    p.add_argument('--chart',type=Path,required=True)
    p.add_argument('--seconds',type=int,default=35,help='Timed session duration including engine loading/preparation; not guaranteed gameplay duration')
    p.add_argument('--full-song',action='store_true',help='Finish the whole chart; exit only after verified natural completion')
    p.add_argument('--watchdog',type=int,default=600,help='Full-song failure timeout, not a successful end condition')
    p.add_argument('--mode',choices=['frontend','frontend-standalone','host','standalone'],default='frontend')
    p.add_argument('--profiles',nargs='+',choices=['baseline','no-bga','discard','uncapped','vsync','directshow','rgb-video'],default=['baseline'])
    p.add_argument('--frame-limit',type=int,default=240)
    p.add_argument('--frame-sync',choices=['VSync','Unlimited'],default='VSync')
    p.add_argument('--window-size',default='1280x800')
    p.add_argument('--output',type=Path)
    p.add_argument('--reuse-runtime',type=Path)
    args=p.parse_args()
    if not 15<=args.seconds<=120:p.error('seconds must be 15-120')
    if not 180<=args.watchdog<=1800:p.error('watchdog must be 180-1800 seconds')
    size=args.window_size.split('x')
    if len(size)!=2 or not all(s.isdigit() for s in size) or not all(320<=int(s)<=7680 for s in size):p.error('window-size must be WIDTHxHEIGHT')
    if args.mode in {'standalone','frontend-standalone'} and args.profiles!=['baseline']:p.error('standalone uses classic renderer settings; profiles are embedded-only')
    chart=args.chart.resolve(strict=True)
    ensure_idle()
    output=(args.output or ROOT/'out/reports/validation'/('performance-'+datetime.now().strftime('%Y%m%d-%H%M%S'))).resolve()
    output.mkdir(parents=True,exist_ok=False)
    source=ROOT/'out/app';protected=digest_protected(source)
    shared=source/'userdata/settings.toml'
    settings=tomllib.loads(shared.read_text(encoding='utf-8-sig')) if shared.exists() else {}
    metadata={'chart':str(chart),'seconds':0 if args.full_song else args.seconds,'full_song':args.full_song,'watchdog_seconds':args.watchdog,'mode':args.mode,'profiles':','.join(args.profiles),
              'frame_limit':args.frame_limit,'frontend_frame_sync':args.frame_sync,'frontend_window':args.window_size}
    for key in ['speed','offset','arrangement','window_width','window_height','chart_encoding']:metadata[key]=settings.get(key,'default')
    with (output/'run-metadata.csv').open('w',newline='',encoding='utf-8') as f:
        writer=csv.writer(f);writer.writerow(['setting','value']);writer.writerows(metadata.items())
    runtime=args.reuse_runtime.resolve(strict=True) if args.reuse_runtime else output/'runtime'
    if args.reuse_runtime:
        if not runtime.is_relative_to((ROOT/'out/reports/validation').resolve()) or not (runtime/'lazerrave-performance-runtime.txt').exists():
            p.error('Reuse requires a marked isolated runtime under out/reports/validation')
    else:
        prepare(source,runtime)
        (runtime/'lazerrave-performance-runtime.txt').write_text('Isolated performance runtime. Not the active player installation.\n',encoding='utf-8')
    configure_window(runtime,args.frame_sync,args.window_size)
    print(f'Isolated runtime: {runtime}',flush=True)
    if args.full_song:print('Full-song test: loading does not shorten gameplay; exits after natural completion. Watchdog expiry is a failure.',flush=True)
    else:print(f'Timed test: automatically exits after {args.seconds} session seconds, including engine loading/preparation. Use the regular package for uninterrupted manual play.',flush=True)
    try:
        for profile in args.profiles:
            ensure_idle()
            case=output/f'{args.mode}-{profile}';case.mkdir()
            run_case(runtime,chart,args.mode,profile,args.seconds,args.frame_limit,case,args.full_song,args.watchdog)
    finally:
        after=digest_protected(source)
        with (output/'protected-files.csv').open('w',newline='',encoding='utf-8') as f:
            writer=csv.writer(f);writer.writerow(['path','sha256_before','sha256_after'])
            for path,value in protected.items():writer.writerow([path,value,after.get(path,'MISSING')])
        if protected!=after:raise RuntimeError('Protected user data changed; inspect protected-files.csv')
        print('Original runtime and shared settings hashes unchanged.',flush=True)
    print(f'Results: {output}',flush=True)

if __name__=='__main__':main()
