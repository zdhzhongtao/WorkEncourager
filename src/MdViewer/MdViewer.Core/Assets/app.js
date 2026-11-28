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
        return runMermaid(containers);
    }

    // 切主题时重画：容器已经存在，按 data-src 恢复源码再跑一遍
    function rerenderMermaid() {
        var nodes = root.querySelectorAll('div.mermaid[data-src]');
        if (nodes.length === 0) return Promise.resolve();
        for (var i = 0; i < nodes.length; i++) {
            // mermaid 会在渲染过的节点上打 data-processed；不清掉它会直接跳过，图就停在源码状态
            nodes[i].removeAttribute('data-processed');
            nodes[i].textContent = nodes[i].getAttribute('data-src') || '';
        }
        return runMermaid(Array.prototype.slice.call(nodes));
    }

    function runMermaid(containers) {
        if (!containers || containers.length === 0) return Promise.resolve();
        for (var p = 0; p < containers.length; p++) containers[p].removeAttribute('data-processed');

        if (!window.mermaid) {
            // 库没加载出来：把源码还回去，至少还能看
            for (var j = 0; j < containers.length; j++) {
                var box = containers[j];
                var pre2 = document.createElement('pre');
                pre2.className = 'mermaid-failed';
                pre2.textContent = box.textContent;
                box.parentNode.replaceChild(pre2, box);
            }
            return Promise.resolve();
        }

        try {
            window.mermaid.initialize({
                startOnLoad: false,
                securityLevel: 'strict',
                theme: 'base',
                // false = 保留图的原始尺寸，大图用容器横向滚动，而不是整体缩到看不清
                useMaxWidth: false,
                fontFamily: getComputedStyle(document.body).fontFamily,
                themeVariables: mermaidTheme()
            });
            // 返回 Promise：调用方要等图真的画完再报 ready（否则标注会按源码文本定位）
            return Promise.resolve(window.mermaid.run({ nodes: containers, suppressErrors: true }))
                .catch(function () { /* 单张图画不出来不影响别的 */ });
        } catch (e) {
            /* 图表语法错就留着源码块 */
            return Promise.resolve();
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

            // 按钮/标签放到 pre 外面的容器里：放 pre 里会跟着横向滚动跑掉，
            // 也会压住第一行代码。
            var wrapper = document.createElement('div');
            wrapper.className = 'code-block';
            pre.parentNode.insertBefore(wrapper, pre);
            wrapper.appendChild(pre);

            if (lang) {
                var label = document.createElement('span');
                label.className = 'code-lang';
                label.setAttribute('data-lang', lang);
                // 文字由 CSS ::after 显示，避免被页内搜索当成正文
                wrapper.appendChild(label);
            }

            highlightBlock(codeEl, lang);

            var btn = document.createElement('button');
            btn.className = 'copy-btn';
            btn.type = 'button';
            btn.setAttribute('aria-label', '复制');
            wrapper.appendChild(btn);

            (function (container, button) {
                button.addEventListener('click', function () {
                    var target = container.querySelector('code');
                    var text = target ? target.textContent : '';
                    var done = function (ok) {
                        button.classList.toggle('done', ok !== false);
                        setTimeout(function () { button.classList.remove('done'); }, 1200);
                    };
                    if (navigator.clipboard && navigator.clipboard.writeText) {
                        navigator.clipboard.writeText(text)
                            .then(function () { done(true); })
                            .catch(function () { fallbackCopy(text, done); });
                    } else {
                        fallbackCopy(text, done);
                    }
                });
            })(wrapper, btn);
        }
    }

    function fallbackCopy(text, done) {
        var area = document.createElement('textarea');
        area.value = text;
        area.style.position = 'fixed';
        area.style.left = '-9999px';
        document.body.appendChild(area);
        area.select();
        var ok = false;
        try { ok = document.execCommand('copy'); } catch (e) { ok = false; }
        document.body.removeChild(area);
        done(ok);
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

    // 表格外面套一层壳方便统计列数 / 加紧凑样式；滚动交给整页，
    // 不再给行数多的表单独加滚动区（见 style.css 里表格段的说明）。
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

            setupTableSort(table);
        }
    }

    // 点表头排序。数值列按数值排，其余按本地化字符串排；第三次点击恢复原始顺序。
    // 表头可能有多行（colspan/rowspan 分组），所以按「逻辑列号」而不是 cellIndex 取列。
    function setupTableSort(table) {
        var head = table.tHead;
        if (!head || !head.rows.length) return;
        var body = table.tBodies && table.tBodies.length ? table.tBodies[0] : null;
        if (!body || body.rows.length < 2) return;

        var original = null;
        var headers = [];
        for (var r = 0; r < head.rows.length; r++) {
            var cells = head.rows[r].cells;
            for (var c = 0; c < cells.length; c++) headers.push(cells[c]);
        }
        if (headers.length === 0) return;

        for (var h = 0; h < headers.length; h++) {
            (function (th) {
                var column = columnIndexOf(th);
                th.classList.add('sortable');
                th.title = '点击排序';
                th.addEventListener('click', function () {
                    sortBy(table, body, column, th);
                });
            })(headers[h]);
        }

        function columnIndexOf(th) {
            var row = th.parentNode;
            var index = 0;
            for (var i = 0; i < row.cells.length; i++) {
                var cell = row.cells[i];
                if (cell === th) return index;
                index += Math.max(1, cell.colSpan || 1);
            }
            return th.cellIndex || 0;
        }

        function snapshot() {
            if (original) return;
            original = [];
            for (var i = 0; i < body.rows.length; i++) original.push(body.rows[i]);
        }

        function sortBy(tbl, tbody, column, th) {
            snapshot();
            var step = th.getAttribute('data-sort');
            var next = step === 'asc' ? 'desc' : (step === 'desc' ? 'none' : 'asc');

            var all = Array.prototype.slice.call(tbody.rows);
            if (next === 'none') {
                for (var r = 0; r < original.length; r++) tbody.appendChild(original[r]);
            } else {
                var sign = next === 'asc' ? 1 : -1;
                var decorated = all.map(function (row, index) { return { row: row, index: index }; });
                decorated.sort(function (a, b) {
                    var result = sign * compare(cellText(a.row, column), cellText(b.row, column));
                    return result !== 0 ? result : a.index - b.index;   // 稳定排序
                });
                for (var k = 0; k < decorated.length; k++) tbody.appendChild(decorated[k].row);
            }

            for (var t = 0; t < headers.length; t++) headers[t].removeAttribute('data-sort');
            if (next !== 'none') th.setAttribute('data-sort', next);

            // 排序把行搬走了，标注的字符偏移随之变化；重新量一遍交给宿主存回去
            var moved = measureAnnotations();
            if (moved.length > 0) post('annotationsMoved', moved);
        }

        function cellText(row, column) {
            var index = 0;
            for (var i = 0; i < row.cells.length; i++) {
                var cell = row.cells[i];
                var span = Math.max(1, cell.colSpan || 1);
                if (column >= index && column < index + span) return (cell.textContent || '').trim();
                index += span;
            }
            return '';
        }

        function compare(a, b) {
            if (a === b) return 0;
            var na = parseNumber(a);
            var nb = parseNumber(b);
            // 百分数与普通数、不同货币不放在一起比大小
            if (na !== null && nb !== null && (a.indexOf('%') >= 0) === (b.indexOf('%') >= 0)) {
                return na < nb ? -1 : 1;
            }
            return a.localeCompare(b, undefined, { numeric: true, sensitivity: 'base' });
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
            // src='' 会被解析成当前文档地址再发一次请求；直接移除属性
            stage.removeAttribute('src');
            stage.alt = '';
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

    // 把一条标注的 [start,end) 重新对齐到当前 DOM。
    // 文档改过之后偏移会漂移：先用记录位置附近的同文匹配，而不是全文第一次出现。
    function nearestIndex(text, quote, hint) {
        if (!quote) return -1;
        var best = -1;
        var bestDistance = Infinity;
        var at = 0;
        while (true) {
            at = text.indexOf(quote, at);
            if (at < 0) break;
            var distance = Math.abs(at - hint);
            if (distance < bestDistance) {
                bestDistance = distance;
                best = at;
            }
            at += Math.max(1, quote.length);
        }
        return best;
    }

    function wholeText(nodes) {
        var text = '';
        for (var i = 0; i < nodes.length; i++) text += nodes[i].nodeValue;
        return text;
    }

    function applyAnnotations(items) {
        clearAnnotations();
        annotations = items || [];
        if (annotations.length === 0) return;

        var nodes = textNodes();
        if (nodes.length === 0) return;
        var whole = wholeText(nodes);

        var ranges = [];
        for (var i = 0; i < annotations.length; i++) {
            var item = annotations[i];
            var start = item.start | 0;
            var end = item.end | 0;
            if (end <= start) continue;

            if (item.quote) {
                var got = whole.substring(start, end);
                if (got !== item.quote) {
                    var at = nearestIndex(whole, item.quote, start);
                    if (at < 0) continue;       // 原文找不到了就跳过，别错位到别处
                    start = at;
                    end = at + item.quote.length;
                }
            }

            if (start < 0 || end > whole.length || end <= start) continue;
            ranges.push({
                start: start,
                end: end,
                id: item.id || String(ranges.length),
                kind: item.kind,
                note: item.note
            });
        }

        // 重叠的标注只保留前一条：包成嵌套 mark 会让 measureAnnotations 读错。
        // 宁可少画一条，也不画到别的词上。
        ranges.sort(function (a, b) { return a.start - b.start || b.end - a.end; });
        var accepted = [];
        var lastEnd = 0;
        for (var r = 0; r < ranges.length; r++) {
            if (ranges[r].start < lastEnd) continue;
            accepted.push(ranges[r]);
            lastEnd = ranges[r].end;
        }

        // 从后往前包：splitText 只影响后面的节点，前面区间的偏移仍然有效。
        for (var k = accepted.length - 1; k >= 0; k--) {
            var range = accepted[k];
            wrapSegments(segments(nodes, range.start, range.end), range.id, range.kind, range.note);
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

    // 三击选中整段 / 选择边界落在元素上时，取容器里第一个（或最后一个）文本节点
    function boundaryText(nodes, container, isStart) {
        if (!container) return null;
        if (container.nodeType === 3) return container;
        var found = null;
        for (var i = 0; i < nodes.length; i++) {
            if (!container.contains(nodes[i])) continue;
            if (isStart) return nodes[i];
            found = nodes[i];
        }
        return found;
    }

    function selectionInfo() {
        var sel = window.getSelection();
        if (!sel || sel.isCollapsed || sel.rangeCount === 0) return null;

        var range = sel.getRangeAt(0);
        if (!root.contains(range.commonAncestorContainer)) return null;

        var nodes = textNodes();
        if (nodes.length === 0) return null;

        var starts = new Array(nodes.length);
        var pos = 0;
        for (var i = 0; i < nodes.length; i++) {
            starts[i] = pos;
            pos += nodes[i].nodeValue.length;
        }

        function indexOfNode(node) {
            for (var j = 0; j < nodes.length; j++) if (nodes[j] === node) return j;
            return -1;
        }

        var start = -1;
        var end = -1;
        var si = range.startContainer.nodeType === 3 ? indexOfNode(range.startContainer) : -1;
        if (si >= 0) start = starts[si] + range.startOffset;
        var ei = range.endContainer.nodeType === 3 ? indexOfNode(range.endContainer) : -1;
        if (ei >= 0) end = starts[ei] + range.endOffset;

        if (start < 0) {
            var first = boundaryText(nodes, range.startContainer, true);
            if (first) start = starts[indexOfNode(first)];
        }
        if (end < 0) {
            var last = boundaryText(nodes, range.endContainer, false);
            if (last) end = starts[indexOfNode(last)] + last.nodeValue.length;
        }
        if (start < 0 || end < 0 || end <= start) return null;

        var whole = wholeText(nodes);
        var text = whole.substring(start, end);
        if (!text || !text.trim()) return null;
        if (text.length > 4000) return null;

        // 注意：quote 必须用计算出来的区间，不能用 sel.toString()。
        // 跨代码块时 sel 会带上"复制"按钮和语言标签的文字，偏移就对不上了。
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

    // 只取「属于这个块自己」的文本：li 里嵌套的 ul/子 li、li 里包的 p 交给子块读，
    // 否则父 li 和子 li 会把同一段读两遍。
    function directText(el) {
        var out = '';
        for (var node = el.firstChild; node; node = node.nextSibling) {
            if (node.nodeType === 3) {
                out += node.nodeValue;
            } else if (node.nodeType === 1) {
                var tag = node.tagName;
                if (tag === 'UL' || tag === 'OL' || tag === 'LI' || tag === 'P' || tag === 'PRE' ||
                    tag === 'TABLE' || tag === 'BLOCKQUOTE' || tag === 'DIV' || tag === 'SECTION') continue;
                out += node.textContent || '';
            }
        }
        return out.replace(/\s+/g, ' ').trim();
    }

    function collectReadableBlocks() {
        var blocks = [];
        var nodes = root.querySelectorAll('h1,h2,h3,h4,h5,h6,p,li,th,td');
        for (var i = 0; i < nodes.length; i++) {
            var el = nodes[i];
            if (el.parentNode && el.parentNode.closest && el.parentNode.closest('pre, .mermaid')) continue;

            var text = directText(el);
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
            if (!speech.active) { delayedSpeak(0, 0); return; }
            speech.paused = false;
            delayedSpeak(speech.index, speech.offset);
            return;
        }

        // play：从头读，段落列表重新收集（改过字号、切过主题后位置会变）
        resetSpeech();
        delayedSpeak(0, 0);
    }

    // cancel() 之后马上 speak()，Chromium 有概率把新语句吞掉；隔一个 tick 再开口。
    function delayedSpeak(index, offset) {
        setTimeout(function () {
            if (speech.paused) return;
            speakFrom(index, offset);
        }, 30);
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

    var anchorSections = null;
    var anchorHeadings = null;

    function currentAnchor() {
        // EPUB：整本拼在一页里，用章节 section 判断读到第几章
        if (!anchorSections) anchorSections = document.querySelectorAll('section.epub-chapter[id]');
        var sections = anchorSections;
        if (sections.length > 0) {
            var top = -1;
            for (var i = 0; i < sections.length; i++) {
                if (sections[i].getBoundingClientRect().top <= 96) top = i;
                else break;
            }
            if (top < 0) top = 0;
            return sections[top].id;
        }

        if (!anchorHeadings) anchorHeadings = document.querySelectorAll('h1[id], h2[id], h3[id]');
        var headings = anchorHeadings;
        if (headings.length === 0) return '';
        var index = -1;
        for (var j = 0; j < headings.length; j++) {
            if (headings[j].getBoundingClientRect().top <= 80) index = j;
            else break;
        }
        if (index < 0) index = 0;
        return headings[index].id;
    }

    var lastRatioPost = 0;
    var lastRatioValue = -1;
    var lastAnchor = null;

    function onScroll() {
        if (ticking) return;
        ticking = true;
        window.requestAnimationFrame(function () {
            var ratio = scrollRatio();
            if (barFill) {
                barFill.style.width = (ratio * 100).toFixed(2) + '%';
                if (bar) bar.classList.toggle('on', ratio > 0.005);
            }
            hideBubble();   // 选区气泡是 fixed 定位，滚动后必须收掉

            var anchor = currentAnchor();
            if (anchor !== lastAnchor) {
                lastAnchor = anchor;
                post('heading', anchor);
            }

            // 宿主关窗时要用「最后一次读到的位置」同步落盘，所以定期回报一次比例
            var now = Date.now();
            if (now - lastRatioPost > 700 && Math.abs(ratio - lastRatioValue) > 0.002) {
                lastRatioPost = now;
                lastRatioValue = ratio;
                post('scrollRatio', ratio.toFixed(4));
            }
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

        // 图表是按主题配色画出来的 SVG，换主题要按 data-src 重画一遍。
        // 注意不能只找 `pre > code`：第一次渲染后代码块已经被换成 div.mermaid 了。
        if (root.querySelector('div.mermaid[data-src]')) {
            rerenderMermaid();
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

    // 恢复阅读位置：图片陆续加载会让 scrollHeight 变化，隔一小会儿再校一次。
    // 但用户一旦自己滚了（滚轮 / 触摸 / 键盘 / 拖滚动条），后面的校正必须放弃，
    // 否则会出现「我往下滚，它又把我拽回去」。
    var restoreTimers = [];
    var restoreDetach = null;

    function cancelRestore() {
        for (var i = 0; i < restoreTimers.length; i++) clearTimeout(restoreTimers[i]);
        restoreTimers = [];
        if (restoreDetach) {
            var detach = restoreDetach;
            restoreDetach = null;
            detach();
        }
    }

    function restoreScroll(y, ratio) {
        cancelRestore();

        var expectedTop = -1;
        var apply = function () {
            var doc = document.documentElement;
            var max = Math.max(0, (doc.scrollHeight || 0) - window.innerHeight);
            var top = 0;
            if (y > 0) top = y;
            else if (ratio > 0) top = ratio * max;
            expectedTop = Math.min(top, max);
            window.scrollTo(0, expectedTop);
        };

        apply();
        restoreTimers.push(setTimeout(apply, 260));
        restoreTimers.push(setTimeout(apply, 900));

        var stop = function () {
            for (var i = 0; i < restoreTimers.length; i++) clearTimeout(restoreTimers[i]);
            restoreTimers = [];
            var detach = restoreDetach;
            restoreDetach = null;
            if (detach) detach();
        };
        var detach = function () {
            window.removeEventListener('wheel', stop);
            window.removeEventListener('touchstart', stop);
            window.removeEventListener('keydown', stop);
            window.removeEventListener('mousedown', stop);
            window.removeEventListener('scroll', onScrollCheck);
        };
        // 用户拖滚动条 / 用脚本滚走时不会触发 wheel，只能看滚动位置有没有偏离我们设的目标
        var onScrollCheck = function () {
            if (expectedTop >= 0 && Math.abs(window.scrollY - expectedTop) > 8) stop();
        };
        restoreDetach = detach;

        window.addEventListener('wheel', stop, { passive: true });
        window.addEventListener('touchstart', stop, { passive: true });
        window.addEventListener('keydown', stop);
        window.addEventListener('mousedown', stop);
        window.addEventListener('scroll', onScrollCheck, { passive: true });
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

    // 无干扰模式：宿主把工具栏和状态栏藏起来后，正文两侧留白可以再收一点。
    // 宿主发的是字符串 "1"/"0"，不能直接 !!on —— "0" 是真值。
    function setZen(on) {
        var enabled = on === true || on === 1 || on === '1' || on === 'true';
        document.body.classList.toggle('zen', enabled);
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
                // 兼容旧协议：payload 被二次序列化成字符串时，先解一层
                if (typeof data === 'string') {
                    try { data = JSON.parse(data) || {}; } catch (e) { data = {}; }
                }
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
        },
        // 导出 HTML：直接快照当前已经渲染好的 DOM —— 公式是 KaTeX 生成的节点、
        // 图表是内联 SVG、代码是 hljs 着色后的 span，不再依赖任何外部脚本。
        exportHtml: function (epubRoot) {
            var clone = document.documentElement.cloneNode(true);

            var junk = clone.querySelectorAll('script, link[rel="stylesheet"], .bubble, #readbar, .copy-btn, .code-lang, .lightbox');
            for (var i = 0; i < junk.length; i++) {
                if (junk[i].parentNode) junk[i].parentNode.removeChild(junk[i]);
            }

            // 阅读器自己的 CSP 会把对方浏览器里的远程图片挡掉，导出页不要带它
            var meta = clone.querySelector('meta[http-equiv="Content-Security-Policy"]');
            if (meta && meta.parentNode) meta.parentNode.removeChild(meta);

            var nonced = clone.querySelectorAll('[nonce]');
            for (var n = 0; n < nonced.length; n++) nonced[n].removeAttribute('nonce');

            var mdHost = /^https:\/\/md\d+\.local\//i;
            var epubHost = /^https:\/\/epub\d+\.local\//i;
            var elements = clone.querySelectorAll('[src], [href]');
            for (var e = 0; e < elements.length; e++) {
                var el = elements[e];
                for (var a = 0; a < 2; a++) {
                    var attr = a === 0 ? 'src' : 'href';
                    var value = el.getAttribute(attr);
                    if (!value) continue;
                    try {
                        if (mdHost.test(value)) {
                            el.setAttribute(attr, decodeURIComponent(value.replace(mdHost, '')));
                        } else if (epubRoot && epubHost.test(value)) {
                            el.setAttribute(attr, epubRoot + decodeURIComponent(value.replace(epubHost, '')));
                        }
                    } catch (err) {
                        // 路径里有过奇怪的 % 就保持原样
                    }
                }
            }

            var body = clone.querySelector('body');
            if (body) {
                body.classList.remove('zen', 'capturing', 'tts-active');
                body.classList.add('for-export');
            }

            return '<!DOCTYPE html>\n' + clone.outerHTML;
        }
    };

    // 把页面里的按键翻译成宿主命令。宿主那套快捷键在正文获得焦点后收不到键，
    // 必须由页面转发（见 MainWindow.Shortcuts.cs）。
    function shortcutAction(e) {
        if (e.isComposing) return null;
        var ctrl = e.ctrlKey || e.metaKey;
        var shift = e.shiftKey;
        var alt = e.altKey;
        var key = e.key;

        if (ctrl && shift && (key === 'e' || key === 'E')) return 'editor';
        if (ctrl && shift && (key === 'd' || key === 'D')) return 'theme';
        if (ctrl && shift && (key === 'w' || key === 'W')) return 'wide';
        if (ctrl && shift && (key === 'r' || key === 'R')) return 'speak';
        if (ctrl && shift && (key === 'b' || key === 'B')) return 'bookmark';
        if (ctrl && shift && (key === 's' || key === 'S')) return 'exportPng';

        if (ctrl && !shift && (key === 'o' || key === 'O')) return 'open';
        if (ctrl && !shift && (key === 'f' || key === 'F')) return 'find';
        if (ctrl && !shift && (key === 'p' || key === 'P')) return 'exportPdf';
        if (ctrl && !shift && (key === 'e' || key === 'E')) return 'recent';
        if (ctrl && (key === ',' || key === '，')) return 'settings';
        if (ctrl && !shift && (key === 't' || key === 'T')) return 'toc';
        if (ctrl && (key === '=' || key === '+')) return 'fontUp';
        if (ctrl && (key === '-' || key === '_')) return 'fontDown';
        if (ctrl && key === '0') return 'fontReset';

        if (key === 'F11') return 'zen';
        if (key === 'F5') return 'reload';
        if (alt && key === 'ArrowLeft') return 'back';
        if (alt && key === 'ArrowRight') return 'forward';
        if (key === 'Escape') return 'escape';
        return null;
    }

    function forwardShortcut(e) {
        var action = shortcutAction(e);
        if (!action) return;
        if (action !== 'escape') e.preventDefault();
        post('shortcut', action);
    }

    function forwardWheel(e) {
        if (!e.ctrlKey) return;
        e.preventDefault();
        post('shortcut', e.deltaY < 0 ? 'fontUp' : 'fontDown');
    }

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
        // mermaid 要在 setupCodeBlocks 之后（它会把 mermaid 代码块整个换掉）。
        // 它返回 Promise：等 SVG 真的落地再报 ready，标注才不会按源码文本定位。
        var mermaidReady = renderMermaid();
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
            document.addEventListener('keydown', forwardShortcut, true);
            window.addEventListener('wheel', forwardWheel, { passive: false });
            document.addEventListener('mousedown', function (e) {
                if (bubble && !bubble.contains(e.target)) hideBubble();
            });
        }

        window.addEventListener('scroll', onScroll, { passive: true });
        onScroll();
        if (mermaidReady && typeof mermaidReady.then === 'function') {
            mermaidReady.then(notifyReady, notifyReady);
        } else {
            notifyReady();
        }
    });
})();
