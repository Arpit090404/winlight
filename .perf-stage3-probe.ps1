param([int]$ProcessId,[ValidateSet('NoQuery','Varied')][string]$Mode='NoQuery',[int]$Cycles=50,[int]$ShowDelayMs=35)
$ErrorActionPreference='Stop'
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Probe3 {
 [StructLayout(LayoutKind.Explicit,Size=40)] public struct Input {
  [FieldOffset(0)] public uint Type; [FieldOffset(8)] public ushort Key;
  [FieldOffset(12)] public uint Flags;
 }
 public delegate bool EnumProc(IntPtr w,IntPtr p);
 [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p,IntPtr x);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr w,out uint p);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr w);
 [DllImport("user32.dll")] static extern uint SendInput(uint n,Input[] a,int s);
 public static bool Visible(uint pid){ bool found=false; EnumWindows((w,x)=>{
  GetWindowThreadProcessId(w,out uint p); if(p==pid&&IsWindowVisible(w)){found=true;return false;}
  return true; },IntPtr.Zero); return found; }
 static Input K(ushort key,uint flags)=>new Input{Type=1,Key=key,Flags=flags};
 public static uint Chord(){var a=new[]{K(0x12,0),K(0x20,0),K(0x20,2),K(0x12,2)};
  return SendInput((uint)a.Length,a,Marshal.SizeOf<Input>());}
 public static uint Word(string text){var a=new Input[text.Length*2];for(int i=0;i<text.Length;i++){
  ushort k=(ushort)Char.ToUpperInvariant(text[i]);a[i*2]=K(k,0);a[i*2+1]=K(k,2);}
  return SendInput((uint)a.Length,a,Marshal.SizeOf<Input>());}
}
'@
function Sample([string]$name){$p=Get-Process -Id $ProcessId;$p.Refresh();
 [pscustomobject]@{Phase=$name;WS=[math]::Round($p.WorkingSet64/1MB,2);
 Private=[math]::Round($p.PrivateMemorySize64/1MB,2);Handles=$p.HandleCount;
 Threads=$p.Threads.Count;CPU=$p.CPU}|ConvertTo-Json -Compress}
Sample 'start';$notVisible=0
for($i=1;$i -le $Cycles;$i++){
 if([Probe3]::Visible([uint32]$ProcessId)){[Probe3]::Chord()|Out-Null;Start-Sleep -Milliseconds 25}
 [Probe3]::Chord()|Out-Null;Start-Sleep -Milliseconds $ShowDelayMs
 if(-not [Probe3]::Visible([uint32]$ProcessId)){$notVisible++}
 for($wait=0;$wait -lt 300 -and -not [Probe3]::Visible([uint32]$ProcessId);$wait+=5){Start-Sleep -Milliseconds 5}
 Start-Sleep -Milliseconds 50
 if($Mode -eq 'Varied'){$word=@('c','calc','vsc','note','xyz')[($i-1)%5];[Probe3]::Word($word)|Out-Null;Start-Sleep -Milliseconds 25}
 [Probe3]::Chord()|Out-Null;Start-Sleep -Milliseconds 25
 if($i -eq [int]($Cycles/2)){Sample 'mid'}
}
Sample 'end';"not_visible_after_35ms=$notVisible;cycles=$Cycles;mode=$Mode"
