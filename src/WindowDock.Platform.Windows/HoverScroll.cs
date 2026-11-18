#nullable disable
using System;
using System.Runtime.InteropServices;
using System.Threading;

using System.Drawing;

namespace WindowDock
{
    internal sealed class HoverTarget
    {
        public readonly IntPtr Root, Handle;
        public readonly Rectangle Bounds;
        public readonly bool External;
        public HoverTarget(IntPtr root,IntPtr handle,Rectangle bounds,bool external)
        { Root=root; Handle=handle; Bounds=bounds; External=external; }
    }

    internal sealed class HoverScroll : IDisposable
    {
        HoverTarget[] targets=new HoverTarget[0];
        readonly IntPtr owner;
        readonly uint wheelLines;
        readonly ConsoleHoverInput consoleInput;
        readonly System.Collections.Generic.Dictionary<IntPtr,double> consoleRemainders=new();
        readonly ManualResetEvent ready=new ManualResetEvent(false);
        readonly Thread thread;
        Native.MouseHookProc callback;
        IntPtr hook;
        uint threadId;
        bool disposed;
        volatile bool enabled=true;
        public bool Installed { get { return hook!=IntPtr.Zero; } }
        public int LastPostError, InstallError, WheelEvents, ForwardedEvents;
        public HoverScroll(IntPtr owner)
        {
            this.owner=owner;
            consoleInput = new(owner);
            wheelLines=Native.WheelScrollLines();
            // A dedicated pump keeps external application resize / paint work off the hook path.
            thread=new Thread(Run) { IsBackground=true, Name="WindowDock hover wheel" };
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
            ready.WaitOne(1500);
        }
        public bool Enabled { get { return enabled; } set { enabled=value; } }
        public void Update(HoverTarget[] snapshot) { Interlocked.Exchange(ref targets,snapshot??new HoverTarget[0]); }
        void Run()
        {
            threadId=Native.GetCurrentThreadId();
            callback=Hook;
            try
            {
                hook=Native.SetWindowsHookEx(14,callback,Native.GetModuleHandle(null),0);
                if(hook==IntPtr.Zero) { InstallError=Marshal.GetLastWin32Error(); Store.Log("Hover wheel installation failed: "+InstallError); }
                ready.Set();
                if(hook!=IntPtr.Zero) NativeMessagePump.Run();
            }
            catch(Exception ex) { Store.Log("Hover wheel: "+ex.Message); }
            finally
            {
                if(hook!=IntPtr.Zero) Native.UnhookWindowsHookEx(hook);
                hook=IntPtr.Zero;
                ready.Set();
            }
        }
        IntPtr Hook(int code,IntPtr message,IntPtr data)
        {
            int id=message.ToInt32();
            if(code>=0 && enabled && (id==Native.VerticalWheel || id==Native.HorizontalWheel))
            {
                Interlocked.Increment(ref WheelEvents);
                Native.MouseHookData mouse=(Native.MouseHookData)Marshal.PtrToStructure(data,typeof(Native.MouseHookData));
                int delta=unchecked((short)(mouse.MouseData>>16));
                if(TryForward((uint)id,mouse.Position,delta,-1)) { Interlocked.Increment(ref ForwardedEvents); return new IntPtr(1); }
            }
            return Native.CallNextHookEx(hook,code,message,data);
        }
        static int MouseModifiers()
        {
            int keys=0;
            if(Native.GetAsyncKeyState(1)<0) keys|=1;
            if(Native.GetAsyncKeyState(2)<0) keys|=2;
            if(Native.GetAsyncKeyState(0x10)<0) keys|=4;
            if(Native.GetAsyncKeyState(0x11)<0) keys|=8;
            if(Native.GetAsyncKeyState(4)<0) keys|=0x10;
            return keys;
        }
        internal bool TryForward(uint message,Native.Point point,int delta,int modifiers,bool externalOnly=false)
        {
            if(!enabled || disposed || (message!=Native.VerticalWheel && message!=Native.HorizontalWheel) || delta==0) return false;
            HoverTarget[] snapshot=targets;
            bool inScope=false;
            foreach(HoverTarget target in snapshot) if(target.Bounds.Contains(point.X,point.Y)) { inScope=true; break; }
            if(!inScope) return false;
            // Use the actual hit window so a dialog or another app above the pane is never scrolled through.
            IntPtr hit=Native.DeepWindowAt(point);
            if(hit==IntPtr.Zero) return false;
            IntPtr root=Native.GetAncestor(hit,2);
            foreach(HoverTarget target in snapshot)
            {
                if((externalOnly && !target.External) || !target.Bounds.Contains(point.X,point.Y) || root!=target.Root ||
                   !Native.IsWindowVisible(target.Handle) || !Native.IsWindowEnabled(target.Root)) continue;
                IntPtr destination;
                if(target.External)
                {
                    if(Native.GetWindow(target.Root,4)!=owner ||
                       (hit!=target.Root && !Native.IsChild(target.Root,hit))) continue;
                    destination=hit;
                }
                else
                {
                    if(hit!=target.Handle && !Native.IsChild(target.Handle,hit)) continue;
                    destination=target.Handle;
                }
                if(modifiers<0) modifiers=MouseModifiers();
                if (target.External && modifiers == 0 && Native.GetForegroundWindow() != target.Root)
                {
                    var windowClass = Native.ClassName(target.Root);
                    // Windows Terminal has a wheel handler on its XAML host window.
                    if (windowClass == Native.TerminalWindowClass) destination = target.Root;
                    if (windowClass == Native.ConsoleWindowClass)
                    {
                        if (message == Native.VerticalWheel && Native.VerticalScroll(target.Root, out var scroll) &&
                            scroll.Page > 0 && scroll.Maximum - scroll.Minimum + 1 > scroll.Page)
                            return ScrollConsole(target.Root, delta);
                        if (consoleInput.TrySend(target.Root, message, point, delta)) return true;
                    }
                }
                // Posting wheel messages does not activate the target or change keyboard focus.
                if(Native.PostWheel(destination,message,point,delta,modifiers)) return true;
                Interlocked.Exchange(ref LastPostError,Marshal.GetLastWin32Error());
                return false; // Do not eat native input when UIPI or another restriction rejects it.
            }
            return false;
        }
        bool ScrollConsole(IntPtr window,int delta)
        {
            // Classic consoles can discard inactive wheel input when an agent owns mouse mode.
            // Their native scrollback remains addressable without changing foreground/focus.
            lock(consoleRemainders)
            {
                double remainder; consoleRemainders.TryGetValue(window,out remainder);
                bool pages=wheelLines==uint.MaxValue;
                remainder+=(double)delta*(pages?1:wheelLines)/120;
                remainder=Math.Clamp(remainder,-512,512);
                int steps=(int)remainder; consoleRemainders[window]=remainder-steps;
                if(consoleRemainders.Count>128) { consoleRemainders.Clear(); consoleRemainders[window]=remainder-steps; }
                uint command=(uint)(pages?(steps>0?2:3):(steps>0?0:1));
                for(int index=0;index<Math.Min(512,Math.Abs(steps));index++)
                    if(!Native.PostMessage(window,0x0115,new IntPtr(command),IntPtr.Zero))
                    { Interlocked.Exchange(ref LastPostError,Marshal.GetLastWin32Error()); return false; }
                return true;
            }
        }
        internal string Describe(Native.Point point)
        {
            var hit=Native.DeepWindowAt(point); var root=Native.GetAncestor(hit,2);
            return "point="+point.X+","+point.Y+" hit="+hit+":"+Native.ClassName(hit)+" root="+root+" owner="+Native.GetWindow(root,4)+" enabled="+enabled+" installed="+Installed+" postError="+LastPostError+" targets="+string.Join(";",Array.ConvertAll(targets,t=>t.Root+":"+t.Bounds+":enabled="+Native.IsWindowEnabled(t.Root)+":visible="+Native.IsWindowVisible(t.Handle)));
        }
        public void Dispose()
        {
            if(disposed) return;
            disposed=true; enabled=false; Update(null);
            consoleInput.Dispose();
            if(threadId!=0) Native.PostThreadMessage(threadId,0x0012,IntPtr.Zero,IntPtr.Zero);
            if(Thread.CurrentThread!=thread) thread.Join(750);
            if(!thread.IsAlive) ready.Dispose();
        }
    }
}
