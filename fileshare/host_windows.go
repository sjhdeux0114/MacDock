package main

/*
#include <stdlib.h>
*/
import "C"

import (
	"encoding/json"
	"fmt"
	"net"
	"net/http"
	"net/http/httptest"
	"path/filepath"
	"strconv"
	"strings"
	"sync"
	"time"
	"unsafe"
)

// The dock owns one server for its lifetime. Settings also work while it is stopped.
type host struct {
	app    *App
	server *http.Server
	port   int
}

var desktop host
var desktopLock sync.Mutex

func (h *host) request(method, route, body string) (map[string]any, error) {
	r := httptest.NewRequest(method, "http://localhost:"+strconv.Itoa(h.port)+route, strings.NewReader(body))
	r.RemoteAddr = "127.0.0.1:0"
	r.Header.Set("X-FileShare", "1")
	h.app.auth.Lock()
	r.AddCookie(&http.Cookie{Name: "session", Value: h.app.token()})
	h.app.auth.Unlock()
	w := httptest.NewRecorder()
	h.app.ServeHTTP(w, r)
	var result map[string]any
	if err := json.Unmarshal(w.Body.Bytes(), &result); err != nil {
		return nil, err
	}
	if w.Code != http.StatusOK {
		return nil, fmt.Errorf("%v", result["error"])
	}
	return result, nil
}

func (h *host) command(folder, op, payload string) (map[string]any, error) {
	if h.app == nil {
		app, err := newConfiguredApp(filepath.Join(folder, "data"), "", "", filepath.Join(folder, "FileShare.ini"))
		if err != nil {
			return nil, err
		}
		h.app = app
	}
	switch op {
	case "save":
		if _, err := h.request("POST", "/api/settings", payload); err != nil {
			return nil, err
		}
	case "start", "auto":
		h.app.auth.Lock()
		cfg := h.app.config
		h.app.auth.Unlock()
		if h.server == nil && !cfg.NeedsSetup && (op == "start" || cfg.AutoStart) {
			listener, err := net.Listen("tcp", ":"+strconv.Itoa(cfg.Port))
			if err != nil {
				return nil, fmt.Errorf("포트 %d에서 서버를 시작할 수 없습니다. %w", cfg.Port, err)
			}
			s := &http.Server{Handler: h.app, ReadHeaderTimeout: 30 * time.Second, IdleTimeout: 90 * time.Second, MaxHeaderBytes: 32 << 10}
			h.server, h.port = s, listener.Addr().(*net.TCPAddr).Port
			go s.Serve(listener)
		}
	case "stop":
		if h.server != nil {
			if err := h.server.Close(); err != nil {
				return nil, err
			}
			h.server, h.port = nil, 0
		}
	case "info":
	default:
		return nil, fmt.Errorf("알 수 없는 서버 명령입니다.")
	}
	info, err := h.request("GET", "/api/info", "")
	if err != nil {
		return nil, err
	}
	info["running"] = h.server != nil
	info["activePort"] = h.port
	if h.server == nil {
		info["phoneUrls"] = []string{}
	}
	return info, nil
}

//export MacDockShare
func MacDockShare(folder, op, payload *C.char) *C.char {
	desktopLock.Lock()
	defer desktopLock.Unlock()
	info, err := desktop.command(C.GoString(folder), C.GoString(op), C.GoString(payload))
	if err != nil {
		info = map[string]any{"error": err.Error()}
	}
	data, _ := json.Marshal(info)
	return C.CString(string(data))
}

//export MacDockShareFree
func MacDockShareFree(p *C.char) { C.free(unsafe.Pointer(p)) }
func main()                      {}
