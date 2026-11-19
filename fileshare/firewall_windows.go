package main

import (
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strconv"
	"syscall"
	"unsafe"
)

// Only the fixed netsh operation is elevated. The installer owns the named rule;
// portable builds have no such rule and keep their existing firewall policy.
const updatePortScript = `
$ErrorActionPreference = 'Stop'
$exe = $env:MACDOCK_FIREWALL_EXE
$port = [int]$env:MACDOCK_FIREWALL_PORT
$policy = New-Object -ComObject HNetCfg.FwPolicy2
$rules = @($policy.Rules | Where-Object { $_.Name -match '^MacDock FileShare LAN [0-9a-f]{64}$' -and $_.ApplicationName -eq $exe -and $_.Direction -eq 1 -and $_.Action -eq 1 -and $_.Protocol -eq 6 })
if ($rules.Count -eq 0) { exit 0 }
if ($rules.Count -ne 1) { exit 1 }
$rule = $rules[0]
if ($rule.LocalPorts -eq [string]$port) { exit 0 }
$argsLine = 'advfirewall firewall set rule name="{0}" program="{1}" dir=in new protocol=TCP localport={2}' -f $rule.Name,$exe,$port
$system = [Environment]::SystemDirectory
$p = Start-Process -Verb RunAs -FilePath (Join-Path $system 'netsh.exe') -WorkingDirectory $system -ArgumentList $argsLine -WindowStyle Hidden -Wait -PassThru
exit $p.ExitCode
`

func updateFirewallPort(port int) error {
	exe, err := os.Executable()
	if err != nil {
		return err
	}
	var directory [260]uint16
	n, _, err := syscall.NewLazyDLL("kernel32.dll").NewProc("GetSystemDirectoryW").Call(uintptr(unsafe.Pointer(&directory[0])), uintptr(len(directory)))
	if n == 0 || n >= uintptr(len(directory)) {
		return fmt.Errorf("Windows 시스템 경로 확인 실패: %v", err)
	}
	system := syscall.UTF16ToString(directory[:])
	cmd := exec.Command(filepath.Join(system, "WindowsPowerShell", "v1.0", "powershell.exe"), "-NoProfile", "-NonInteractive", "-Command", updatePortScript)
	cmd.Dir = system
	cmd.Env = append(os.Environ(), "MACDOCK_FIREWALL_EXE="+exe, "MACDOCK_FIREWALL_PORT="+strconv.Itoa(port))
	cmd.SysProcAttr = &syscall.SysProcAttr{HideWindow: true}
	return cmd.Run()
}
