(() => {
    const { t } = window.ui;
    const $ = id => document.getElementById(id);
    const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
    const canRun = document.body.dataset.canRun === 'true';
    const admin = document.body.dataset.admin === 'true';
    let jobs = [];
    const node = (tag, text, cls) => { const el = document.createElement(tag); el.textContent = text; if (cls) el.className = cls; return el; };
    function feedback(message, bad = false, link) {
        const box = $('feedback'); box.replaceChildren(node('span', message)); box.hidden = false; box.className = bad ? 'bad' : '';
        if (link) { const a = node('a', t('查看执行详情 ↗')); a.href = link; box.append(a); }
        const local = document.querySelector('dialog[open] .dialog-feedback');
        if (local) { local.textContent = message; local.hidden = false; local.className = 'dialog-feedback' + (bad ? ' bad' : ''); }
    }
    async function api(url, method = 'GET', body) {
        const headers = { 'X-CSRF-TOKEN': token };
        if (body && !(body instanceof FormData)) { headers['Content-Type'] = 'application/json'; body = JSON.stringify(body); }
        const response = await fetch(url, { method, headers, body, credentials: 'same-origin' });
        const text = await response.text();
        let result; try { result = text ? JSON.parse(text) : null; } catch { result = null; }
        if (!response.ok) throw new Error(window.ui.error(result?.error || result?.title || t("请求失败 ({0})", response.status)));
        return result;
    }
    let plugins = { versions: [], workers: [] };
    let page = 1;
    function render() {
        const search = $('plugin-search').value.trim().toLocaleLowerCase();
        const status = $('plugin-status').value;
        const filtered = plugins.versions.slice().reverse().filter(plugin =>
            (!status || plugin.status === status) &&
            [plugin.id, plugin.version, ...(plugin.jobs || [])].join(' ').toLocaleLowerCase().includes(search));
        const pages = Math.max(1, Math.ceil(filtered.length / 20));
        page = Math.min(page, pages);
        $('plugin-count').textContent = t("共 {0} 个版本 · 第 {1} / {2} 页 · 每页 20 条", filtered.length, page, pages);
        $('previous-page').disabled = page <= 1;
        $('next-page').disabled = page >= pages;
        $('plugins').replaceChildren();
        if (!plugins.versions.length) $('plugins').append(node('p', t('还没有插件。上传示例 ZIP 开始。'), 'muted'));
        else if (!filtered.length) $('plugins').append(node('p', t('没有符合筛选条件的插件。'), 'muted'));
        const pluginTable = node('table', '', 'execution-table');
        const pluginHead = node('thead', ''), pluginHeadRow = node('tr', ''), pluginBody = node('tbody', '');
        for (const title of [t('插件 / 功能'), t('版本'), t('状态'), t('操作')]) pluginHeadRow.append(node('th', title));
        pluginHead.append(pluginHeadRow); pluginTable.append(pluginHead, pluginBody);
        if (filtered.length) $('plugins').append(pluginTable);
        for (const plugin of filtered.slice((page - 1) * 20, page * 20)) {
            const row = node('tr', ''), name = node('td', ''), item = node('td', ''), actions = node('td', '');
            name.append(node('strong', plugin.id), node('div', (plugin.jobs || []).join('、'), 'hint'));
            row.append(name, node('td', plugin.version), item, actions);
            const labels = { Pending: t('待加载'), Active: t('已激活'), Failed: t('加载失败'), Superseded: t('历史版本') };
            item.append(node('span', labels[plugin.status] || plugin.status, `badge ${plugin.status}`));
            if (plugin.error) item.append(node('p', plugin.error, 'error'));
            const workers = plugins.workers.filter(w => w.pluginId === plugin.id && w.version === plugin.version && Date.now() - Date.parse(w.heartbeatUtc) < 20000);
            if (plugin.status === 'Active') item.append(node('p', workers.some(w => w.status === 'Loaded') ? t('Worker 已加载') : t('暂无在线 Worker 加载确认'), 'hint'));
            for (const worker of workers.filter(w => w.status === 'Failed')) item.append(node('p', worker.error, 'error'));
            if (admin) {
                const button = node('button', t('删除'), 'delete-plugin'); button.type = 'button';
                button.onclick = async () => {
                    if (!confirm(t("删除插件 {0} / {1}？删除激活版本后不再提供该插件任务，也不会自动切回旧版本。正在执行的任务继续完成；尚未开始的任务和周期执行在没有激活版本时会失败。DLL 文件保留在磁盘。", plugin.id, plugin.version))) return;
                    button.disabled = true;
                    try {
                        await api(`/api/plugins/${plugin.sequence}`, 'DELETE');
                        feedback(t("插件 {0} / {1} 已删除。", plugin.id, plugin.version));
                        await refresh();
                    } catch (e) { feedback(e.message, true); button.disabled = false; }
                };
                actions.append(button);
            } else actions.append(node('span', t('只读'), 'muted'));
            pluginBody.append(row);
        }
    }
    async function refresh() { plugins = await api('/api/plugins'); render(); }
    $('add-plugin')?.addEventListener('click', () => {
        $('upload-dialog').querySelector('.dialog-feedback').hidden = true;
        $('upload-dialog').showModal();
    });
    $('close-upload')?.addEventListener('click', () => $('upload-dialog').close());
    $('upload-form')?.addEventListener('submit', async event => {
        event.preventDefault(); const button = event.currentTarget.querySelector('button'); button.disabled = true;
        try {
            const body = new FormData(); body.append('file', $('package').files[0]);
            const result = await api('/api/plugins', 'POST', body);
            $('upload-dialog').close();
            $('upload-form').reset();
            $('plugin-search').value = ''; $('plugin-status').value = ''; page = 1;
            feedback(t("{0} {1} 上传成功，加载状态请查看列表。", result.id, result.version));
            await refresh();
        }
        catch (e) { feedback(e.message, true); } finally { button.disabled = false; }
    });
    async function poll() { try { await refresh(); } catch (e) { feedback(e.message, true); } finally { setTimeout(poll, 5000); } }
    $('plugin-search').addEventListener('input', () => { page = 1; render(); });
    $('plugin-status').addEventListener('change', () => { page = 1; render(); });
    $('previous-page').addEventListener('click', () => { page--; render(); });
    $('next-page').addEventListener('click', () => { page++; render(); });
    window.addEventListener('languagechange', () => { render(); });
    poll();
})();
