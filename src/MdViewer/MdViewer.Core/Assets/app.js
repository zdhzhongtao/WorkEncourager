// MdViewer 页面脚本。
//   · 代码块「复制」按钮 + 语言标签 + 轻量语法高亮
//   · 阅读进度条
//   · 滚动时回报当前章节给宿主
//   · 接收宿主的主题 / 字号 / 滚动位置 / 跳转指令
// 只用 postMessage 通道，不 eval 任何来自文档的内容。
(function () {
    'use strict';

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

    // ───────────────────────── 语法高亮 ─────────────────────────

    var COMMON = 'if else for while do switch case break continue return function class new try catch finally throw ' +
        'import from export default public private protected static void true false null undefined this super ' +
        'const let var struct enum interface extends implements async await yield delete in of typeof instanceof';

    var KEYWORDS = {
        js: COMMON + ' NaN Infinity require module exports',
        javascript: COMMON + ' NaN Infinity require module exports',
        jsx: COMMON,
        ts: COMMON + ' type interface enum namespace declare readonly abstract as any unknown never',
        typescript: COMMON + ' type interface enum namespace declare readonly abstract as any unknown never',
        java: COMMON + ' package final abstract synchronized volatile transient native',
        cs: COMMON + ' using namespace internal sealed partial override virtual readonly params ref out base is as',
        csharp: COMMON + ' using namespace internal sealed partial override virtual readonly params ref out base is as',
        c: COMMON + ' include define typedef sizeof unsigned signed long short char float double goto',
        cpp: COMMON + ' include define typedef sizeof unsigned signed long short char float double goto template typename',
        go: 'break case chan const continue default defer else fallthrough for func go goto if import interface map ' +
            'package range return select struct switch type var nil true false',
        rust: 'as async await break const continue crate dyn else enum extern false fn for if impl in let loop match ' +
            'mod move mut pub ref return self Self static struct super trait true type unsafe use where while',
        python: 'and as assert async await break class continue def del elif else except finally for from global if ' +
            'import in is lambda nonlocal not or pass raise return try while with yield True False None self',
        py: 'and as assert async await break class continue def del elif else except finally for from global if ' +
            'import in is lambda nonlocal not or pass raise return try while with yield True False None self',
        sql: 'select from where and or not insert into values update set delete create table drop alter add column ' +
            'index join left right inner outer on group by order having limit offset union all distinct as case when ' +
            'then else end null is like between exists primary key foreign references default',
        bash: 'if then else elif fi for while do done case esac function return exit echo export local readonly source cd',
        sh: 'if then else elif fi for while do done case esac function return exit echo export local readonly source cd',
        shell: 'if then else elif fi for while do done case esac function return exit echo export local readonly source cd',
        powershell: 'if elseif else foreach for while do switch function param return begin process end try catch finally ' +
            'throw filter class enum static using',
        ps1: 'if elseif else foreach for while do switch function param return begin process end try catch finally throw',
        json: 'true false null',
        yaml: 'true false null yes no on off',
        yml: 'true false null yes no on off',
        css: '',
        scss: '',
        html: '',
        xml: '',
        toml: 'true false',
        ini: 'true false'
    };

    var LINE_COMMENTS = {
        python: ['#'], py: ['#'], ruby: ['#'], perl: ['#'], bash: ['#'], sh: ['#'], shell: ['#'],
        powershell: ['#'], ps1: ['#'], yaml: ['#'], yml: ['#'], toml: ['#'], ini: ['#'], conf: ['#'],
        dockerfile: ['#'], makefile: ['#'], r: ['#'],
        sql: ['--'], lua: ['--'],
        css: [], scss: [], html: [], xml: []
    };

    function linePrefixes(lang) {
        if (LINE_COMMENTS[lang]) return LINE_COMMENTS[lang];
        if (lang === 'css' || lang === 'scss' || lang === 'html' || lang === 'xml') return [];
        return ['//'];
    }

    function keywordsOf(lang) {
        var raw = KEYWORDS[lang];
        if (raw === undefined) raw = COMMON;
        var map = Object.create(null);
        raw.split(/\s+/).forEach(function (word) { if (word) map[word] = 1; });
        return map;
    }

    // 一个正则把「注释 / 字符串 / 数字 / 标识符」一次扫出来，按命中顺序拼回 html。
    function tokenize(code, lang) {
        var prefixes = linePrefixes(lang);
        var keywords = keywordsOf(lang);
        var blockComment = lang !== 'python' && lang !== 'py' && lang !== 'yaml' && lang !== 'yml' &&
            lang !== 'bash' && lang !== 'sh' && lang !== 'powershell' && lang !== 'ps1';

        // 拼出「行注释前缀」的交替分支
        var linePart = '';
        if (prefixes.length > 0) {
            var pieces = prefixes.map(function (p) {
                return escapeReg(p) + '[^\\n]*';
            });
            linePart = pieces.join('|') + '|';
        }

        var pattern = new RegExp(
            '(' + linePart +
            (blockComment ? '/\\*[\\s\\S]*?\\*/|' : '') +
            '"(?:\\\\.|[^"\\\\\\n])*"|' +
            "'(?:\\\\.|[^'\\\\\\n])*'|" +
            '`(?:\\\\.|[^`\\\\])*`|' +
            '\\b\\d[\\w.]*\\b|' +
            '[A-Za-z_$][\\w$]*' +
            ')', 'g');

        var out = '';
        var last = 0;
        var match;

        while ((match = pattern.exec(code)) !== null) {
            var token = match[0];
            if (match.index > last) out += escapeHtml(code.slice(last, match.index));
            last = match.index + token.length;

            var first = token.charAt(0);
            var isComment = false;
            for (var i = 0; i < prefixes.length; i++) {
                if (token.indexOf(prefixes[i]) === 0) { isComment = true; break; }
            }
            if (blockComment && token.indexOf('/*') === 0) isComment = true;

            if (isComment) {
                out += '<span class="tok-com">' + escapeHtml(token) + '</span>';
            } else if (first === '"' || first === "'" || first === '`') {
                out += '<span class="tok-str">' + escapeHtml(token) + '</span>';
            } else if (first >= '0' && first <= '9') {
                out += '<span class="tok-num">' + escapeHtml(token) + '</span>';
            } else if (keywords[token]) {
                out += '<span class="tok-kw">' + escapeHtml(token) + '</span>';
            } else {
                // 后面紧跟左括号的标识符当函数名处理
                var rest = code.slice(last);
                if (/^\s*\(/.test(rest)) out += '<span class="tok-fn">' + escapeHtml(token) + '</span>';
                else out += escapeHtml(token);
            }
        }
        if (last < code.length) out += escapeHtml(code.slice(last));
        return out;
    }

    function escapeReg(text) {
        return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    }

    // ───────────────────────── 代码块外壳 ─────────────────────────

    function setupCodeBlocks() {
        var blocks = document.querySelectorAll('pre');
        for (var i = 0; i < blocks.length; i++) {
            var pre = blocks[i];
            var codeEl = pre.querySelector('code');
            var lang = '';
            if (codeEl && codeEl.className) {
                var m = /language-([\w+#-]+)/.exec(codeEl.className);
                if (m) { lang = m[1].toLowerCase(); }
            }
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

    function highlightBlock(codeEl, lang) {
        if (!codeEl || !lang) return;
        if (codeEl.getAttribute('data-hl') === '1') return;
        var raw = codeEl.textContent || '';
        if (raw.length === 0 || raw.length > 120000) return;
        if (lang === 'text' || lang === 'txt' || lang === 'plain' || lang === 'plaintext') return;
        if (lang === 'html' || lang === 'xml' || lang === 'svg') return; // 标签太多，不值得，反而更花
        codeEl.setAttribute('data-hl', '1');
        codeEl.innerHTML = tokenize(raw, lang);
    }

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

    // 表格外面套一层可横向滚动的壳。顺手记下列数：列多的表在 CSS 里换紧凑排版，
    // 30 列这种宽表才不至于一列挤成一个字。壳上的 data-cols 也方便排查问题。
    function wrapTables() {
        var tables = document.querySelectorAll('table');
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
        }
    }

    function markBroken(img) {
        if (img.classList.contains('img-failed')) return;
        img.classList.add('img-failed');
        img.alt = (img.getAttribute('alt') || '图片') + '（加载失败）';
    }

    function markFailedImages() {
        var images = document.querySelectorAll('img');
        for (var i = 0; i < images.length; i++) {
            (function (img) {
                img.addEventListener('error', function () { markBroken(img); });
                // 有些图在 DOMContentLoaded 之前就已经失败了，error 事件不会再触发一次
                if (img.complete && img.naturalWidth === 0) markBroken(img);
            })(images[i]);
        }
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
        var root = document.documentElement;
        root.setAttribute('data-theme', String(id));
        root.setAttribute('data-mode', mode);

        if (document.body) {
            document.body.setAttribute('data-theme', String(id));
            document.body.setAttribute('data-mode', mode);
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
    // 宽屏时给 body 挂 wide 类，把左右留白也收窄一些，屏幕才算真的用满。
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
            }
        });
    }

    // 宿主通过 ExecuteScriptAsync 调这里取当前位置
    window.__mdviewer = {
        getScroll: function () {
            return JSON.stringify({
                y: window.scrollY || 0,
                ratio: scrollRatio(),
                height: (document.documentElement || {}).scrollHeight || 0
            });
        },
        setScroll: function (y) { restoreScroll(Number(y) || 0, 0); },
        anchor: function () { return currentAnchor(); }
    };

    function notifyReady() {
        post('ready', {
            title: document.title,
            headings: document.querySelectorAll('h1[id], h2[id], h3[id]').length,
            chapters: document.querySelectorAll('section.epub-chapter').length
        });
    }

    document.addEventListener('DOMContentLoaded', function () {
        // 导出的 HTML 是给别人在浏览器里看的，不需要阅读进度条
        var exporting = !!(document.body && document.body.classList.contains('for-export'));
        if (!exporting) makeBar();
        listenHost();
        setupCodeBlocks();
        wrapTables();
        markFailedImages();
        window.addEventListener('scroll', onScroll, { passive: true });
        onScroll();
        notifyReady();
    });
})();
