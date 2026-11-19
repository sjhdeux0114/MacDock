package main

import (
	"encoding/json"
	"errors"
	"io"
	"net"
	"net/http"
	"net/http/cookiejar"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"testing"
)

func TestDockServerLifecycle(t *testing.T) {
	var h host
	folder := t.TempDir()
	op := func(action, payload string) map[string]any {
		t.Helper()
		state, err := h.command(folder, action, payload)
		if err != nil {
			t.Fatal(err)
		}
		return state
	}
	first := op("start", "")
	defer func() { h.command(folder, "stop", ""); h.app.root.Close() }()
	if first["running"] != false || first["needsSetup"] != true {
		t.Fatal("first run must wait for settings")
	}
	listener, err := net.Listen("tcp4", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	port := listener.Addr().(*net.TCPAddr).Port
	listener.Close()
	root := t.TempDir()
	data, _ := json.Marshal(map[string]any{"sharedRoot": root, "port": port, "password": "dock-test-password"})
	op("save", string(data))
	if op("start", "")["running"] != true {
		t.Fatal("server did not start")
	}
	base := "http://127.0.0.1:" + strconv.Itoa(port)
	jar, _ := cookiejar.New(nil)
	client := &http.Client{Jar: jar}
	login, _ := http.NewRequest("POST", base+"/api/login", strings.NewReader(`{"password":"dock-test-password"}`))
	login.Header.Set("X-FileShare", "1")
	res, err := client.Do(login)
	if err != nil {
		t.Fatal(err)
	}
	res.Body.Close()
	if res.StatusCode != 200 {
		t.Fatalf("login: %d", res.StatusCode)
	}
	if err := os.WriteFile(filepath.Join(root, "sample.txt"), []byte("dock transfer"), 0600); err != nil {
		t.Fatal(err)
	}
	res, err = client.Get(base + "/api/content?path=sample.txt")
	if err != nil {
		t.Fatal(err)
	}
	defer res.Body.Close()
	b, err := io.ReadAll(res.Body)
	if err != nil || string(b) != "dock transfer" {
		t.Fatalf("download: %d, %s, %v", res.StatusCode, b, err)
	}
	if op("stop", "")["running"] != false {
		t.Fatal("server did not stop")
	}
	if op("start", "")["activePort"] != port {
		t.Fatal("restart did not reuse port")
	}
}

func TestFirewallFailurePreservesSettings(t *testing.T) {
	var h host
	folder := t.TempDir()
	if _, err := h.command(folder, "info", ""); err != nil {
		t.Fatal(err)
	}
	defer h.app.root.Close()
	previous := h.app.config
	h.app.changeFirewallPort = func(port int) error {
		if port != 9123 {
			t.Fatalf("unexpected firewall port %d", port)
		}
		return errors.New("approval cancelled")
	}
	payload, _ := json.Marshal(map[string]any{"sharedRoot": t.TempDir(), "port": 9123, "password": "test-password"})
	if _, err := h.command(folder, "save", string(payload)); err == nil {
		t.Fatal("expected failure")
	}
	saved, err := readINI(h.app.configPath)
	if err != nil {
		t.Fatal(err)
	}
	for _, c := range []Config{h.app.config, saved} {
		if c.Port != previous.Port || c.SharedRoot != previous.SharedRoot || c.PasswordHash != previous.PasswordHash || !c.NeedsSetup {
			t.Fatal("previous settings were not restored")
		}
	}
}
