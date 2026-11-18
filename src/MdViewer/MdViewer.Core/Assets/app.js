// MdViewer 页面脚本。
//   · 数学公式（KaTeX）、Mermaid 图表、代码高亮（highlight.js）—— 库都是按需加载的
//   · 图片点击放大、表格表头吸附与点击排序
//   · 标注高亮 / 书签定位 / 朗读
//   · 阅读进度条、滚动时回报当前章节
//   · 接收宿主的主题 / 字号 / 字体 / 宽度 / 滚动位置 / 跳转指令
// 只用 postMessage 通道，不 eval 任何来自文档的内容。
(function () {
    'use strict';

    // ───────────────────────── 基础 ─────────────────────────

    function post(kind, payload) {
        try {
            if (window.chrome && window.chrome.webview) {
                window.chrome.webview.postMessage({ kind: kind, payload: payload });
            }
        } catch (e) { /* 宿主没准备好就算了 */ }
    }

    function escapeHtml(text) {
        return String(text)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;');
    }

    var root = document.querySelector('.markdown-body') || document.body;
    var exporting = !!(document.body && document.body.classList.contains('for-export'));

    // ───────────────────────── 数学公式 ─────────────────────────

    // Markdig 的数学扩展输出 <span class="math">\(…\)</span> 和 <div class="math">\[…\]</div>，
    // 内容里已经带了定界符，KaTeX 认这个格式（delimiters 参数）。
    function renderMath() {
        if (!window.katex) return;
        var nodes = root.querySelectorAll('.math');
        for (var i = 0; i < nodes.length; i++) {
            var el = nodes[i];
            var tex = stripMathDelimiters((el.textContent || '').trim());
            if (!tex) continue;
            var display = el.tagName === 'DIV';
            try {
                el.innerHTML = '';
                window.katex.render(tex, el, {
                    displayMode: display,
                    throwOnError: false,
                    trust: false
                });
                if (display) el.classList.add('math-display');
            } catch (e) {
                // 渲染失败就保留原始 TeX 文本，比整页空白强
            }
        }
    }

    // Markdig 的 UseMathematics 会把公式包成 \( ... \) / \[ ... \]，
    // 但 katex.render 要的是裸 TeX —— 带着定界符传进去会被当成非法命令，整块标红。
    function stripMathDelimiters(tex) {
        if (!tex) return tex;

        var pairs = [['\\(', '\\)'], ['\\[', '\\]'], ['$$', '$$'], ['$', '$']];
        for (var i = 0; i < pairs.length; i++) {
            var left = pairs[i][0], right = pairs[i][1];
            if (tex.length > left.length + right.length &&
                tex.substring(0, left.length) === left &&
                tex.substring(tex.length - right.length) === right) {
                return tex.substring(left.length, tex.length - right.length).trim();
            }
        }
        return tex;
    }

    // ───────────────────────── Mermaid ─────────────────────────

    function renderMermaid() {
        var blocks = root.querySelectorAll('pre > code.language-mermaid, pre > code.lang-mermaid');
        if (blocks.length === 0) return;

        var containers = [];
        for (var i = 0; i < blocks.length; i++) {
            var code = blocks[i];
            var pre = code.parentNode;
            var div = document.createElement('div');
            div.className = 'mermaid';
            div.setAttribute('data-src', code.textContent || '');
            div.textContent = code.textContent || '';
            pre.parentNode.replaceChild(div, pre);
            containers.push(div);
        }

        if (!window.mermaid) {
            // 库没加载出来：把源码还回去，至少还能看
            for (var j = 0; j < containers.length; j++) {
                var box = containers[j];
                var pre2 = document.createElement('pre');
                pre2.className = 'mermaid-failed';
                pre2.textContent = box.textContent;
                box.parentNode.replaceChild(pre2, box);
            }
            return;
        }

        try {
            window.mermaid.initialize({
                startOnLoad: false,
                securityLevel: 'strict',
                theme: 'base',
                fontFamily: getComputedStyle(document.body).fontFamily,
                themeVariables: mermaidTheme()
            });
            window.mermaid.run({ nodes: containers, suppressErrors: true });
        } catch (e) {
            /* 图表语法错就留着源码块 */
        }
    }

    // 把当前主题的 CSS 变量翻译成 mermaid 的 themeVariables，图表才跟正文一个色系
    function mermaidTheme() {
        var s = getComputedStyle(document.documentElement);
        function v(name, fallback) {
            var value = (s.getPropertyValue(name) || '').trim();
            return value || fallback;
        }
        return {
            background: v('--bg', '#ffffff'),
            primaryColor: v('--card', '#f6f8fa'),
            primaryTextColor: v('--fg', '#1f2328'),
            primaryBorderColor: v('--line', '#d8dee4'),
            secondaryColor: v('--quote-bg', '#f6f8fa'),
            tertiaryColor: v('--bg', '#ffffff'),
            lineColor: v('--fg-muted', '#8b949e'),
            textColor: v('--fg', '#1f2328'),
            mainBkg: v('--card', '#f6f8fa'),
            nodeBorder: v('--line', '#d8dee4'),
            clusterBkg: v('--bg', '#ffffff'),
            clusterBorder: v('--line', '#d8dee4'),
            titleColor: v('--fg', '#1f2328'),
            edgeLabelBackground: v('--bg', '#ffffff')
        };
    }

    // ───────────────────────── 代码块 ─────────────────────────

    function langOf(codeEl) {
        if (!codeEl || !codeEl.className) return '';
        var m = /language-([\w+#.-]+)/.exec(codeEl.className) || /lang-([\w+#.-]+)/.exec(codeEl.className);
        return m ? m[1].toLowerCase() : '';
    }

    function setupCodeBlocks() {
        var blocks = root.querySelectorAll('pre');
        for (var i = 0; i < blocks.length; i++) {
            var pre = blocks[i];
            var codeEl = pre.querySelector('code');
            var lang = langOf(codeEl);

            // mermaid 的代码块交给 renderMermaid，别给它挂复制按钮和语言标签
            if (lang === 'mermaid') continue;

            if (lang) {
                var label = document.createElement('span');
                label.className = 'code-lang';
                label.textContent = lang;
                pre.appendChild(label);
            }

            highlightBlock(codeEl, lang);

            var btn = document.createElement('button');
            btn.className = 'copy-btn';
            btn.type = 'button';
            btn.textContent = '复制';
            btn.addEventListener('click', function () {
                var target = this.parentNode.querySelector('code');
                var text = target ? target.textContent : '';
                if (navigator.clipboard && navigator.clipboard.writeText) {
                    navigator.clipboard.writeText(text);
                } else {
                    var area = document.createElement('textarea');
                    area.value = text;
                    document.body.appendChild(area);
                    area.select();
                    try { document.execCommand('copy'); } catch (e) { }
                    document.body.removeChild(area);
                }
                var self = this;
                self.textContent = '已复制';
                setTimeout(function () { self.textContent = '复制'; }, 1200);
            });
            pre.appendChild(btn);
        }
    }

    // highlight.js 认 class="language-xxx"；加载不出来就什么都不做（保留纯文本）
    function highlightBlock(codeEl, lang) {
        if (!codeEl || !lang) return;
        if (codeEl.getAttribute('data-hl') === '1') return;
        var raw = codeEl.textContent || '';
        if (raw.length === 0 || raw.length > 200000) return;
        if (lang === 'text' || lang === 'txt' || lang === 'plain' || lang === 'plaintext') return;

        if (window.hljs) {
            try {
                codeEl.setAttribute('data-hl', '1');
                codeEl.classList.add('hljs');
                window.hljs.highlightElement(codeEl);
                return;
            } catch (e) {
                // 掉下去，用纯文本显示
            }
        }
        codeEl.setAttribute('data-hl', '1');
    }

    // ───────────────────────── 表格 ─────────────────────────

    // 数一张表的列数（前几行取最大值，处理表头有 colspan 的情况）
    function countColumns(table) {
        var rows = table.rows;
        var most = 0;
        var limit = Math.min(rows.length, 4);
        for (var r = 0; r < limit; r++) {
            var cells = rows[r].cells;
            var total = 0;
            for (var c = 0; c < cells.length; c++) total += cells[c].colSpan || 1;
            if (total > most) most = total;
        }
        return most;
    }

    // 表格外面套一层可横向滚动的壳。列多的表在 CSS 里换紧凑排版，
    // 30 列这种宽表才不至于一列挤成一个字。壳上的 data-cols 也方便排查问题。
    function wrapTables() {
        var tables = root.querySelectorAll('table');
        for (var i = 0; i < tables.length; i++) {
            var table = tables[i];
            var wrap = (table.parentNode && table.parentNode.classList &&
                        table.parentNode.classList.contains('table-wrap'))
                ? table.parentNode : null;
            if (!wrap) {
                wrap = document.createElement('div');
                wrap.className = 'table-wrap';
                table.parentNode.insertBefore(wrap, table);
                wrap.appendChild(table);
            }

            var cols = countColumns(table);
            if (cols > 0) {
                wrap.setAttribute('data-cols', cols);
                if (cols >= 8) wrap.classList.add('table-compact');
            }

            // 行数多的表：让表格自己滚、表头吸附住。
            // （普通表格不这么做 —— 见 style.css 里 .table-tall 的说明）
            if (table.rows.length > 18) wrap.classList.add('table-tall');

            setupTableSort(table);
        }
    }

    // 点表头排序。数值列按数值排，其余按本地化字符串排；第三次点击恢复原始顺序。
    function setupTableSort(table) {
        var head = table.tHead;
        if (!head || !head.rows.length) return;
        var body = table.tBodies && table.tBodies.length ? table.tBodies[0] : null;
        if (!body || body.rows.length < 2) return;

        var original = null;
        var cells = head.rows[head.rows.length - 1].cells;
        for (var c = 0; c < cells.length; c++) {
            (function (index, th) {
                th.classList.add('sortable');
                th.title = '点击排序';
                th.addEventListener('click', function () {
                    sortBy(table, body, index, th);
                });
            })(c, cells[c]);
        }

        function snapshot() {
            if (original) return;
            original = [];
            for (var i = 0; i < body.rows.length; i++) original.push(body.rows[i]);
        }

        function sortBy(tbl, tbody, index, th) {
            snapshot();
            var step = th.getAttribute('data-sort');
            var next = step === 'asc' ? 'desc' : (step === 'desc' ? 'none' : 'asc');

            var all = Array.prototype.slice.call(tbody.rows);
            if (next === 'none') {
                for (var r = 0; r < original.length; r++) tbody.appendChild(original[r]);
            } else {
                var sign = next === 'asc' ? 1 : -1;
                all.sort(function (a, b) {
                    return sign * compare(cellText(a, index), cellText(b, index));
                });
                for (var k = 0; k < all.length; k++) tbody.appendChild(all[k]);
            }

            for (var t = 0; t < cells.length; t++) cells[t].removeAttribute('data-sort');
            if (next !== 'none') th.setAttribute('data-sort', next);
        }

        function cellText(row, index) {
            var cell = row.cells[index];
            return cell ? (cell.textContent || '').trim() : '';
        }

        function compare(a, b) {
            if (a === b) return 0;
            // 去掉千分位、货币符号和百分号再试数字
            var na = parseNumber(a);
            var nb = parseNumber(b);
            if (na !== null && nb !== null) return na < nb ? -1 : 1;
            return a.localeCompare(b, 'zh-CN', { numeric: true, sensitivity: 'base' });
        }

        function parseNumber(text) {
            if (!text) return null;
            var cleaned = text.replace(/[,，\s￥$€£%]/g, '');
            if (!/^-?\d+(\.\d+)?$/.test(cleaned)) return null;
            var value = parseFloat(cleaned);
            return isNaN(value) ? null : value;
        }
    }

    // ───────────────────────── 图片 ─────────────────────────

    function markBroken(img) {
        if (img.classList.contains('img-failed')) return;
        img.classList.add('img-failed');
        img.alt = (img.getAttribute('alt') || '图片') + '（加载失败）';
    }

    function markFailedImages() {
        var images = root.querySelectorAll('img');
        for (var i = 0; i < images.length; i++) {
            (function (img) {
                img.addEventListener('error', function () { markBroken(img); });
                // 有些图在 DOMContentLoaded 之前就已经失败了，error 事件不会再触发一次
                if (img.complete && img.naturalWidth === 0) markBroken(img);
            })(images[i]);
        }
    }

    // 点图片放大：全屏遮罩 + 滚轮缩放 + 拖动 + Esc 关闭。
    // 文档里的图经常是架构图 / 长截图，按 100% 看是刚需。
    function setupLightbox() {
        var images = root.querySelectorAll('img');
        if (images.length === 0) return;

        var overlay = null, stage = null, current = 0, scale = 1, dragging = false;
        var startX = 0, startY = 0, originX = 0, originY = 0, offsetX = 0, offsetY = 0;

        for (var i = 0; i < images.length; i++) {
            (function (img) {
                img.classList.add('zoomable');
                img.addEventListener('click', function (e) {
                    if (img.classList.contains('img-failed')) return;
                    e.preventDefault();
                    open(img);
                });
            })(images[i]);
        }

        function ensure() {
            if (overlay) return;
            overlay = document.createElement('div');
            overlay.className = 'lightbox';
            stage = document.createElement('img');
            stage.className = 'lightbox-img';
            overlay.appendChild(stage);
            var hint = document.createElement('div');
            hint.className = 'lightbox-hint';
            hint.textContent = '滚轮缩放 · 拖动平移 · 双击还原 · Esc 关闭';
            overlay.appendChild(hint);
            document.body.appendChild(overlay);

            overlay.addEventListener('click', function (e) {
                if (e.target === overlay) close();
            });
            overlay.addEventListener('wheel', function (e) {
                e.preventDefault();
                zoom(e.deltaY < 0 ? 1.12 : 1 / 1.12);
            }, { passive: false });
            overlay.addEventListener('dblclick', function () { reset(); });
            overlay.addEventListener('mousedown', function (e) {
                dragging = true;
                startX = e.clientX; startY = e.clientY;
                originX = offsetX; originY = offsetY;
                overlay.classList.add('dragging');
            });
            window.addEventListener('mousemove', function (e) {
                if (!dragging) return;
                offsetX = originX + (e.clientX - startX);
                offsetY = originY + (e.clientY - startY);
                applyTransform();
            });
            window.addEventListener('mouseup', function () {
                dragging = false;
                if (overlay) overlay.classList.remove('dragging');
            });
            overlay.addEventListener('dragstart', function (e) { e.preventDefault(); });
        }

        function applyTransform() {
            stage.style.transform = 'translate(' + offsetX + 'px,' + offsetY + 'px) scale(' + scale + ')';
        }

        function zoom(factor) {
            scale = Math.max(0.2, Math.min(8, scale * factor));
            applyTransform();
        }

        function reset() {
            scale = 1; offsetX = 0; offsetY = 0;
            applyTransform();
        }

        function open(img) {
            ensure();
            current = img;
            stage.src = img.currentSrc || img.src;
            stage.alt = img.alt || '';
            reset();
            overlay.classList.add('on');
        }

        function close() {
            if (!overlay) return;
            overlay.classList.remove('on');
            stage.src = '';
        }

        window.__mdviewerLightboxClose = close;
    }

    // ───────────────────────── 标注 / 书签 ─────────────────────────

    // 文本偏移 → DOM Range 的映射。
    // 只认「同一个文本节点内」的区间：跨节点的选区拆成多段分别包 mark，
    // 这样既能处理整段高亮，也不用担心 surroundContents 抛异常。
    function textNodes() {
        var list = [];
        var walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, {
            acceptNode: function (node) {
                var parent = node.parentNode;
                if (!parent) return NodeFilter.FILTER_REJECT;
                var tag = parent.nodeName;
                if (tag === 'SCRIPT' || tag === 'STYLE') return NodeFilter.FILTER_REJECT;
                if (parent.classList && (parent.classList.contains('copy-btn') ||
                                         parent.classList.contains('code-lang'))) return NodeFilter.FILTER_REJECT;
                return NodeFilter.FILTER_ACCEPT;
            }
        });
        var node;
        while ((node = walker.nextNode())) list.push(node);
        return list;
    }

    function fullLength(nodes) {
        var total = 0;
        for (var i = 0; i < nodes.length; i++) total += nodes[i].nodeValue.length;
        return total;
    }

    // 把 [start,end) 换算成一批 {node, from, to}
    function segments(nodes, start, end) {
        var result = [];
        var pos = 0;
        for (var i = 0; i < nodes.length; i++) {
            var len = nodes[i].nodeValue.length;
            var nodeStart = pos;
            var nodeEnd = pos + len;
            pos = nodeEnd;
            if (nodeEnd <= start) continue;
            if (nodeStart >= end) break;
            var from = Math.max(start, nodeStart) - nodeStart;
            var to = Math.min(end, nodeEnd) - nodeStart;
            if (to > from) result.push({ node: nodes[i], from: from, to: to });
        }
        return result;
    }

    function wrapSegments(list, id, kind, note) {
        for (var i = 0; i < list.length; i++) {
            var seg = list[i];
            var node = seg.node;
            if (!node.parentNode) continue;
            try {
                var target = node;
                if (seg.to < target.nodeValue.length) target.splitText(seg.to);
                if (seg.from > 0) target = target.splitText(seg.from);

                var mark = document.createElement('mark');
                mark.className = 'annot' + (kind === 'note' ? ' annot-note' : '');
                mark.setAttribute('data-annot', id);
                if (note) mark.setAttribute('title', note);
                target.parentNode.insertBefore(mark, target);
                mark.appendChild(target);
            } catch (e) {
                // 单个节点包不上就算了，别影响其它段
            }
        }
    }

    function clearAnnotations() {
        var marks = root.querySelectorAll('mark.annot');
        for (var i = 0; i < marks.length; i++) {
            var mark = marks[i];
            var parent = mark.parentNode;
            if (!parent) continue;
            while (mark.firstChild) parent.insertBefore(mark.firstChild, mark);
            parent.removeChild(mark);
            parent.normalize();
        }
    }

    var annotations = [];

    function applyAnnotations(items) {
        clearAnnotations();
        annotations = items || [];
        if (annotations.length === 0) return;

        var nodes = textNodes();
        var total = fullLength(nodes);

        for (var i = 0; i < annotations.length; i++) {
            var item = annotations[i];
            var start = item.start | 0;
            var end = item.end | 0;
            if (end <= start) continue;

            // 文档改过之后偏移会漂移：对不上原文就在全文里找一次
            var segs = segments(nodes, start, end);
            var got = '';
            for (var k = 0; k < segs.length; k++) {
                got += segs[k].node.nodeValue.substr(segs[k].from, segs[k].to - segs[k].from);
            }
            if (item.quote && got !== item.quote) {
                var whole = '';
                for (var n = 0; n < nodes.length; n++) whole += nodes[n].nodeValue;
                if (whole.length !== total) { nodes = textNodes(); }
                var at = whole.indexOf(item.quote);
                if (at >= 0) {
                    start = at;
                    end = at + item.quote.length;
                    segs = segments(nodes, start, end);
                } else {
                    continue; // 原文找不到了，跳过这条，不破坏排版
                }
            }

            wrapSegments(segs, item.id || String(i), item.kind, item.note);
        }
    }

    // 加一次标注后 DOM 变了，偏移会失效。重新从 DOM 里量一遍所有 mark 的位置。
    function measureAnnotations() {
        var marks = root.querySelectorAll('mark.annot');
        if (marks.length === 0) return [];

        var nodes = textNodes();
        var result = [];
        var map = {};
        var pos = 0;

        for (var i = 0; i < nodes.length; i++) {
            var node = nodes[i];
            var len = node.nodeValue.length;
            var mark = node.parentNode && node.parentNode.closest ? node.parentNode.closest('mark.annot') : null;
            if (mark) {
                var id = mark.getAttribute('data-annot');
                if (!map[id]) {
                    map[id] = { id: id, start: pos, end: pos, quote: '', kind: 'highlight', note: mark.getAttribute('title') || '' };
                    result.push(map[id]);
                }
                map[id].end = pos + len;
                map[id].quote += node.nodeValue;
            }
            pos += len;
        }
        return result;
    }

    // 选中文字后浮出来的小工具条：高亮 / 批注 / 朗读
    var bubble = null;

    function selectionInfo() {
        var sel = window.getSelection();
        if (!sel || sel.isCollapsed || sel.rangeCount === 0) return null;
        var text = sel.toString();
        if (!text || !text.trim()) return null;
        if (text.length > 4000) return null;

        var range = sel.getRangeAt(0);
        if (!root.contains(range.commonAncestorContainer)) return null;

        var nodes = textNodes();
        var pos = 0, start = -1, end = -1;
        for (var i = 0; i < nodes.length; i++) {
            var node = nodes[i];
            var len = node.nodeValue.length;
            if (node === range.startContainer) start = pos + range.startOffset;
            if (node === range.endContainer) end = pos + range.endOffset;
            pos += len;
        }
        if (start < 0 || end < 0 || end <= start) return null;
        return { text: text, start: start, end: end, rect: range.getBoundingClientRect() };
    }

    function hideBubble() {
        if (bubble) bubble.classList.remove('on');
    }

    function ensureBubble() {
        if (bubble) return bubble;
        bubble = document.createElement('div');
        bubble.className = 'bubble';
        bubble.innerHTML =
            '<button type="button" data-act="highlight">高亮</button>' +
            '<button type="button" data-act="note">批注</button>' +
            '<button type="button" data-act="speak">朗读</button>';
        document.body.appendChild(bubble);

        bubble.addEventListener('mousedown', function (e) { e.preventDefault(); });
        bubble.addEventListener('click', function (e) {
            var act = e.target && e.target.getAttribute && e.target.getAttribute('data-act');
            if (!act) return;
            var info = selectionInfo();
            if (!info) { hideBubble(); return; }
            if (act === 'speak') {
                speakText(info.text);
            } else {
                post('annotate', { act: act, text: info.text, start: info.start, end: info.end });
            }
            hideBubble();
            var sel = window.getSelection();
            if (sel) sel.removeAllRanges();
        });
        return bubble;
    }

    function showBubble(rect) {
        var el = ensureBubble();
        el.classList.add('on');
        var top = rect.top - el.offsetHeight - 8;
        if (top < 8) top = rect.bottom + 8;
        var left = rect.left + rect.width / 2 - el.offsetWidth / 2;
        left = Math.max(8, Math.min(left, window.innerWidth - el.offsetWidth - 8));
        el.style.top = top + 'px';
        el.style.left = left + 'px';
    }

    // ───────────────────────── 朗读 ─────────────────────────

    // pause()/resume() 在 WebView2（Chromium + Windows SAPI）上不可靠：界面已经显示
    // 「已暂停」，声音还在往下读。所以暂停的做法是取消当前 utterance，自己记住读到
    // 第几段第几个字，继续时从那里重新开口 —— 代价只是那一句会重读一遍，换来的是
    // 「按下去就一定停」。
    var speech = {
        parts: [],
        index: 0,        // 正在读第几段
        offset: 0,       // 这一段已经读到第几个字
        rate: 1,
        active: false,   // 会话还在（朗读中或暂停中）
        paused: false,
        current: null,   // 高亮着的那个元素
        seq: 0,          // 每次取消自增，用来丢弃旧 utterance 的回调
        guard: null      // 看门狗定时器
    };

    function collectReadableBlocks() {
        var blocks = [];
        var nodes = root.querySelectorAll('h1,h2,h3,h4,h5,h6,p,li,blockquote>p,td,th');
        for (var i = 0; i < nodes.length; i++) {
            var el = nodes[i];
            if (el.querySelector('pre, .mermaid')) continue;
            var text = (el.textContent || '').trim();
            if (!text) continue;
            // 太短的（页码、单字）跳过，读起来断断续续
            if (text.length < 2) continue;
            blocks.push({ el: el, text: text });
        }
        return blocks;
    }

    function clearHighlight() {
        if (speech.current && speech.current.classList) speech.current.classList.remove('tts-active');
        speech.current = null;
    }

    function stopGuard() {
        if (speech.guard) {
            clearInterval(speech.guard);
            speech.guard = null;
        }
    }

    // 只清内部状态、不回报：切换文档或从头再读时用，免得状态栏闪一下「朗读结束」
    function resetSpeech() {
        stopGuard();
        speech.seq++;
        if (window.speechSynthesis) {
            try { window.speechSynthesis.cancel(); } catch (e) { }
        }
        clearHighlight();
        speech.active = false;
        speech.paused = false;
        speech.index = 0;
        speech.offset = 0;
        speech.parts = [];
    }

    function stopSpeak() {
        resetSpeech();
        post('speakState', 'stopped');
    }

    // onend 偶尔不回调（语音包坏了、引擎静默丢掉 utterance），没有兜底的话按钮会
    // 永远停在「朗读中」。每秒问一次引擎还在不在说话，连续 5 秒没动静就当读完了。
    function startGuard() {
        stopGuard();
        var idle = 0;
        speech.guard = setInterval(function () {
            if (!speech.active || speech.paused) { idle = 0; return; }
            if (window.speechSynthesis.speaking || window.speechSynthesis.pending) { idle = 0; return; }
            if (++idle >= 5) stopSpeak();
        }, 1000);
    }

    function speakFrom(index, offset) {
        if (!window.speechSynthesis) {
            post('speakState', 'unsupported');
            return;
        }
        if (!speech.parts.length) speech.parts = collectReadableBlocks();

        // 这一段已经读到尾了（或者本来就是空的），顺延到下一段
        while (index < speech.parts.length && offset >= speech.parts[index].text.length) {
            index++;
            offset = 0;
        }
        if (index >= speech.parts.length) { stopSpeak(); return; }

        var part = speech.parts[index];
        var rest = part.text.slice(offset);

        clearHighlight();
        if (part.el && part.el.classList) {
            speech.current = part.el;
            part.el.classList.add('tts-active');

            // 滚到正在读的段落，但不要打断用户自己翻页 —— 只在它离开视口时才动
            var rect = part.el.getBoundingClientRect();
            if (rect.top < 0 || rect.bottom > window.innerHeight) {
                part.el.scrollIntoView({ block: 'center', behavior: 'smooth' });
            }
        }

        var seq = speech.seq;
        var utter = new SpeechSynthesisUtterance(rest);
        utter.lang = /[\u4e00-\u9fa5]/.test(rest) ? 'zh-CN' : 'en-US';
        utter.rate = speech.rate;

        // 读到哪个字了，暂停后就从这个位置接着读。有些语音不给 boundary 事件，
        // 那就退回「整段重读」，总比从头读全文强。
        utter.onboundary = function (e) {
            if (seq !== speech.seq || speech.paused) return;
            if (e && typeof e.charIndex === 'number' && e.charIndex >= 0) {
                speech.offset = offset + e.charIndex;
            }
        };
        utter.onend = function () {
            if (seq !== speech.seq) return;                            // 被暂停或停止取消掉的旧语句
            if (!speech.active || speech.paused) return;
            speakFrom(index + 1, 0);
        };
        utter.onerror = function (e) {
            if (seq !== speech.seq) return;
            if (!speech.active || speech.paused) return;
            var why = e && e.error;
            if (why === 'canceled' || why === 'interrupted') return;    // 自己取消的，不算错
            speakFrom(index + 1, 0);                                   // 这句读不出来，跳过去别卡住
        };

        speech.index = index;
        speech.offset = offset;
        speech.active = true;
        speech.paused = false;
        try { window.speechSynthesis.speak(utter); } catch (e) { stopSpeak(); return; }
        startGuard();
        post('speakState', 'playing');
    }

    // 划词朗读：只读选中的那段，读完就结束
    function speakText(text) {
        if (!window.speechSynthesis) { post('speakState', 'unsupported'); return; }
        if (!text) return;
        resetSpeech();
        speech.parts = [{ el: null, text: text }];
        speakFrom(0, 0);
    }

    function handleSpeak(payload) {
        var action = payload && payload.action ? payload.action : 'play';
        if (payload && typeof payload.rate === 'number' && payload.rate >= 0.5 && payload.rate <= 2) {
            speech.rate = payload.rate;
        }

        if (!window.speechSynthesis) { post('speakState', 'unsupported'); return; }

        if (action === 'stop') { stopSpeak(); return; }

        if (action === 'pause') {
            if (!speech.active) { post('speakState', 'stopped'); return; }
            // 先立暂停标记再取消：cancel 之后 offset 就不会再被 boundary 更新了
            speech.paused = true;
            speech.seq++;
            stopGuard();
            try { window.speechSynthesis.cancel(); } catch (e) { }
            post('speakState', 'paused');
            return;
        }

        if (action === 'resume') {
            if (!speech.active) { speakFrom(0, 0); return; }
            speech.paused = false;
            speakFrom(speech.index, speech.offset);
            return;
        }

        // play：从头读，段落列表重新收集（改过字号、切过主题后位置会变）
        resetSpeech();
        speakFrom(0, 0);
    }

    // ───────────────────────── 进度条 + 章节回报 ─────────────────────────

    var bar = null;
    var barFill = null;
    var ticking = false;

    function makeBar() {
        bar = document.createElement('div');
        bar.id = 'readbar';
        barFill = document.createElement('i');
        bar.appendChild(barFill);
        document.body.appendChild(bar);
    }

    function scrollRatio() {
        var doc = document.documentElement;
        var max = Math.max(0, (doc.scrollHeight || 0) - window.innerHeight);
        if (max <= 0) return 0;
        return Math.min(1, Math.max(0, window.scrollY / max));
    }

    function currentAnchor() {
        // EPUB：整本拼在一页里，用章节 section 判断读到第几章
        var sections = document.querySelectorAll('section.epub-chapter[id]');
        if (sections.length > 0) {
            var top = -1;
            for (var i = 0; i < sections.length; i++) {
                if (sections[i].getBoundingClientRect().top <= 96) top = i;
                else break;
            }
            if (top < 0) top = 0;
            return sections[top].id;
        }

        var headings = document.querySelectorAll('h1[id], h2[id], h3[id]');
        if (headings.length === 0) return '';
        var index = -1;
        for (var j = 0; j < headings.length; j++) {
            if (headings[j].getBoundingClientRect().top <= 80) index = j;
            else break;
        }
        if (index < 0) index = 0;
        return headings[index].id;
    }

    function onScroll() {
        if (ticking) return;
        ticking = true;
        window.requestAnimationFrame(function () {
            var ratio = scrollRatio();
            if (barFill) {
                barFill.style.width = (ratio * 100).toFixed(2) + '%';
                if (bar) bar.classList.toggle('on', ratio > 0.005);
            }
            post('heading', currentAnchor());
            ticking = false;
        });
    }

    // ───────────────────────── 宿主指令 ─────────────────────────

    function applyTheme(payload) {
        // 新协议：{ id: 'nord', mode: 'dark' }；旧协议：直接给 'dark' / 'light'
        var id = null;
        var mode = null;

        if (typeof payload === 'string') {
            mode = payload.toLowerCase() === 'dark' ? 'dark' : 'light';
            id = mode === 'dark' ? 'github-dark' : 'github-light';
        } else if (payload && typeof payload === 'object') {
            id = payload.id;
            mode = payload.mode;
        }
        if (!id) return;
        if (mode !== 'dark' && mode !== 'light') mode = 'light';

        // 主题变量写在 <html> 上（body 继承），data-mode 决定 color-scheme。
        // 两个元素都设一份，方便以后给 body 单独做差异化。
        var el = document.documentElement;
        el.setAttribute('data-theme', String(id));
        el.setAttribute('data-mode', mode);

        if (document.body) {
            document.body.setAttribute('data-theme', String(id));
            document.body.setAttribute('data-mode', mode);
        }

        // 图表是按主题配色画出来的 SVG，换主题要重画一遍
        if (root.querySelector('.mermaid svg')) {
            var sources = root.querySelectorAll('.mermaid');
            for (var i = 0; i < sources.length; i++) {
                var svg = sources[i].querySelector('svg');
                if (!svg) continue;
                var code = sources[i].getAttribute('data-src');
                if (code) sources[i].textContent = code;
            }
            renderMermaid();
        }
    }

    function applyFonts(payload) {
        if (!payload) return;
        var body = document.body;
        if (!body) return;
        set('--font-body', payload.body);
        set('--font-code', payload.code);
        set('--font-heading', payload.heading);

        function set(name, value) {
            if (value === undefined || value === null) return;
            if (String(value).length === 0) body.style.removeProperty(name);
            else body.style.setProperty(name, String(value));
        }
    }

    function scrollToAnchor(id, smooth) {
        if (!id) return;
        var target = document.getElementById(String(id));
        if (!target) return;
        var distance = Math.abs(target.getBoundingClientRect().top);
        // 长距离跳转用 smooth 要等好几秒，直接瞬移更符合直觉
        var behavior = (smooth && distance < window.innerHeight * 3) ? 'smooth' : 'auto';
        target.scrollIntoView({ behavior: behavior, block: 'start' });
    }

    // 恢复阅读位置：图片陆续加载会让 scrollHeight 变化，隔一小会儿再校一次
    function restoreScroll(y, ratio) {
        var apply = function () {
            var doc = document.documentElement;
            var max = Math.max(0, (doc.scrollHeight || 0) - window.innerHeight);
            var top = 0;
            if (y > 0) top = y;
            else if (ratio > 0) top = ratio * max;
            window.scrollTo(0, Math.min(top, max));
        };
        apply();
        setTimeout(apply, 260);
        setTimeout(apply, 900);
    }

    // 正文栏宽度：>0 = 限制在这么多 px（阅读舒适），0 = 铺满窗口（宽屏，给多列表格用）。
    function setContentWidth(width) {
        var body = document.body;
        if (!body) return;
        body.style.setProperty('--content-width', width > 0 ? width + 'px' : 'none');
        if (width > 0) {
            if (body.classList.contains('wide')) body.classList.remove('wide');
        } else if (!body.classList.contains('wide')) {
            body.classList.add('wide');
        }
    }

    // 无干扰模式：宿主把工具栏和状态栏藏起来后，正文两侧留白可以再收一点
    function setZen(on) {
        document.body.classList.toggle('zen', !!on);
    }

    function listenHost() {
        if (!window.chrome || !window.chrome.webview) return;
        window.chrome.webview.addEventListener('message', function (e) {
            var msg = e.data || {};
            var payload = msg.payload;
            if (msg.kind === 'fontSize') {
                var size = parseInt(payload, 10);
                if (size >= 12 && size <= 32) {
                    document.documentElement.style.setProperty('--md-font-size', size + 'px');
                    document.body.style.setProperty('--md-font-size', size + 'px');
                }
            } else if (msg.kind === 'theme') {
                // 切主题只换 html 上的 data-theme，不重新渲染整页，滚动位置才不会跳回顶部
                applyTheme(payload);
            } else if (msg.kind === 'fonts') {
                applyFonts(payload);
            } else if (msg.kind === 'width') {
                var width = parseInt(payload, 10);
                if (width >= 0) setContentWidth(width);
            } else if (msg.kind === 'scrollTo') {
                scrollToAnchor(payload, true);
            } else if (msg.kind === 'scrollTop') {
                window.scrollTo({ top: 0, behavior: 'smooth' });
            } else if (msg.kind === 'restore') {
                var data = payload || {};
                restoreScroll(Number(data.y) || 0, Number(data.ratio) || 0);
            } else if (msg.kind === 'zen') {
                setZen(payload);
            } else if (msg.kind === 'annotations') {
                applyAnnotations(payload);
            } else if (msg.kind === 'speak') {
                handleSpeak(payload);
            } else if (msg.kind === 'scrollY') {
                window.scrollTo({ top: Number(payload) || 0, behavior: 'auto' });
            }
        });
    }

    // 宿主通过 ExecuteScriptAsync 调这里
    window.__mdviewer = {
        getScroll: function () {
            return JSON.stringify({
                y: window.scrollY || 0,
                ratio: scrollRatio(),
                height: (document.documentElement || {}).scrollHeight || 0,
                anchor: currentAnchor()
            });
        },
        setScroll: function (y) { restoreScroll(Number(y) || 0, 0); },
        anchor: function () { return currentAnchor(); },
        // 标注：加完一条之后重新量一遍，宿主拿到的偏移一定是当前 DOM 的
        measure: function () { return JSON.stringify(measureAnnotations()); },
        applyAnnotations: function (json) {
            try { applyAnnotations(JSON.parse(json)); } catch (e) { }
        },
        // 导出 PNG 前把气泡、进度条这些临时 UI 藏掉
        forCapture: function (on) {
            document.body.classList.toggle('capturing', !!on);
        },
        html: function () {
            var clone = document.documentElement.cloneNode(true);
            var junk = clone.querySelectorAll('script, link[rel="stylesheet"], .bubble, #readbar, .copy-btn, .code-lang, .lightbox');
            for (var i = 0; i < junk.length; i++) {
                if (junk[i].parentNode) junk[i].parentNode.removeChild(junk[i]);
            }
            return '<!DOCTYPE html>\n' + clone.outerHTML;
        }
    };

    function notifyReady() {
        post('ready', {
            title: document.title,
            headings: document.querySelectorAll('h1[id], h2[id], h3[id]').length,
            chapters: document.querySelectorAll('section.epub-chapter').length
        });
    }

    // ───────────────────────── 初始化 ─────────────────────────

    document.addEventListener('DOMContentLoaded', function () {
        if (!exporting) makeBar();
        listenHost();

        renderMath();
        setupCodeBlocks();
        // mermaid 要在 setupCodeBlocks 之后（它会把 mermaid 代码块整个换掉）
        renderMermaid();
        wrapTables();
        markFailedImages();
        setupLightbox();

        // 标注 / 朗读 / 气泡只在阅读器里启用（导出的 HTML 没有宿主，用不上）
        if (!exporting) {
            window.addEventListener('mouseup', function (e) {
                if (bubble && bubble.contains(e.target)) return;
                setTimeout(function () {
                    var info = selectionInfo();
                    if (info) showBubble(info.rect);
                    else hideBubble();
                }, 10);
            });
            window.addEventListener('keydown', function (e) {
                if (e.key === 'Escape') {
                    hideBubble();
                    if (window.__mdviewerLightboxClose) window.__mdviewerLightboxClose();
                }
            });
            document.addEventListener('mousedown', function (e) {
                if (bubble && !bubble.contains(e.target)) hideBubble();
            });
        }

        window.addEventListener('scroll', onScroll, { passive: true });
        onScroll();
        notifyReady();
    });
})();
