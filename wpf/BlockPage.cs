using System;
using System.Collections.Generic;
using System.Linq;

namespace MacDock;

internal readonly record struct BlockCell(DockItem Item, int X, int Y, int Width, int Height);

internal sealed class BlockPage
{
    internal readonly List<BlockCell> Cells = new();
    internal int Columns => Cells.Max(c => c.X + c.Width);
    internal int Rows => Cells.Max(c => c.Y + c.Height);

    internal static List<BlockPage> Pack(DockConfig config, bool vertical, int lanes, int limit)
    {
        // A wide button remains horizontal even on a single-column side dock.
        int cross = vertical ? Math.Max(2, lanes) : lanes;
        BlockPage Layout(int start, int length, bool paging, out int consumed)
        {
            int columns = vertical ? cross : length, rows = vertical ? length : cross;
            var used = new bool[columns, rows];
            var page = new BlockPage();
            int controls = paging ? 3 : 1;
            int slots = rows * columns;
            (int x, int y) At(int n) => vertical ? (n / rows, n % rows) : (n % columns, n / columns);
            for (int n = slots - controls; n < slots; n++) {
                var (x,y) = At(n); used[x,y] = true;
            }
            consumed = 0;
            foreach (var item in config.Buttons.Skip(start)) {
                int w = item.BlockSize == "1x1" ? 1 : 2;
                int h = item.BlockSize == "2x2" && lanes >= 2 ? 2 : 1;
                bool placed = false;
                for (int n = 0; n < slots; n++) {
                    var (x,y) = At(n);
                    if (x+w > columns || y+h > rows) continue;
                    if (used[x,y] || (w == 2 && used[x+1,y]) || (h == 2 && (used[x,y+1] || used[x+1,y+1]))) continue;
                    for (int dx=0; dx<w; dx++) for (int dy=0; dy<h; dy++) used[x+dx,y+dy]=true;
                    page.Cells.Add(new(item,x,y,w,h)); consumed++; placed=true; break;
                }
                if (!placed) break;
            }
            void Control(int n,string type,string label,string icon) {
                var (x,y)=At(n);
                page.Cells.Add(new(new DockItem { Type=type, Label=label, Icon=icon, Color="#344052" },x,y,1,1));
            }
            if (paging) {
                Control(slots-3,"previous-page","이전 버튼 페이지",vertical ? "↑" : "‹");
                Control(slots-2,"next-page","다음 버튼 페이지",vertical ? "↓" : "›");
            }
            Control(slots-1,"settings","설정","⚙");
            return page;
        }
        for (int length=1; length<=limit; length++) {
            var candidate=Layout(0,length,false,out int count);
            if (count==config.Buttons.Count) return new() { candidate };
        }
        var pages = new List<BlockPage>();
        for (int start=0; start<config.Buttons.Count;) {
            var next=Layout(start,limit,true,out int count);
            if (count==0) throw new InvalidOperationException("독 버튼을 배치할 화면 공간이 부족합니다.");
            pages.Add(next); start+=count;
        }
        return pages;
    }
}
