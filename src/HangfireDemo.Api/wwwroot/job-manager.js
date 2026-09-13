(() => {
    const { t } = window.ui;
    const $ = id => document.getElementById(id);
    const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
    const canRun = document.body.dataset.canRun === 'true';
    const admin = document.body.dataset.admin === 'true';
    let jobs = [];
    let executionRows = [], executionPage = 1, executionPageSize = 20;
    $('execution-page-size').value = '20';
    const node = (tag, text, cls) => { const el = document.createElement(tag); el.textContent = text; if (cls) el.className = cls; return el; };
    function openDialog(id) {
        const dialog = $(id);
        dialog.querySelector('.dialog-feedback').hidden = true;
        if (!dialog.open) dialog.showModal();
    }

    $('open-create')?.addEventListener('click', () => openDialog('create-dialog'));
    document.querySelectorAll('[data-close]').forEach(button =>
        button.addEventListener('click', () => $(button.dataset.close).close()));
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
    function modeChanged() {
        $('delay-fields').hidden = $('mode').value !== 'Delayed';
        $('recurring-fields').hidden = $('mode').value !== 'Recurring';
        $('delay').required = $('mode').value === 'Delayed';
        $('delay').disabled = $('mode').value !== 'Delayed';
        for (const id of ['schedule-id', 'cron', 'timezone']) {
            $(id).required = $('mode').value === 'Recurring';
            $(id).disabled = $('mode').value !== 'Recurring';
        }
        $('submit-job').textContent = $('mode').value === 'Recurring' ? t('保存周期计划') : t('提交任务');
    }
    function kindChanged() {
        const webApi = $('job-kind').value === 'WebAPI';
        $('plugin-fields').hidden = webApi;
        $('parameter-fields').hidden = webApi;
        $('plugin-version-hint').hidden = webApi;
        $('task').disabled = webApi; $('task').required = !webApi;
        $('parameters').disabled = webApi;
        $('webapi-fields').hidden = !webApi;
        $('webapi-url').disabled = !webApi; $('webapi-url').required = webApi;
    }
    function selectTask() {
        const job = jobs.find(j => `${j.pluginId}/${j.jobId}` === $('task').value);
        $('parameters').value = JSON.stringify(job?.parametersExample ?? {}, null, 2);
    }
    async function refresh() {
        const [available, executions] = await Promise.all([api('/api/plugin-jobs'), api('/api/plugin-executions')]);
        jobs = available;
        if (canRun) {
            const selected = $('task').value;
            $('task').replaceChildren();
            for (const job of jobs) { const option = node('option', `${job.name} · ${job.pluginId}/${job.jobId} · v${job.version}`); option.value = `${job.pluginId}/${job.jobId}`; $('task').append(option); }
            if (!jobs.length) { const option = node('option', t('暂无可用插件任务')); option.value = ''; $('task').append(option); }
            if (jobs.some(j => `${j.pluginId}/${j.jobId}` === selected)) $('task').value = selected;
            else if (!selected && jobs.length) selectTask();
        }
        executionRows = executions;
        renderExecutions();
    }
    function renderExecutions() {
        const pages = Math.max(1, Math.ceil(executionRows.length / executionPageSize));
        executionPage = Math.max(1, Math.min(executionPage, pages));
        const executions = executionRows.slice((executionPage - 1) * executionPageSize, executionPage * executionPageSize);
        $('execution-count').textContent = t('共 {0} 个任务 · 第 {1} / {2} 页', executionRows.length, executionPage, pages);
        $('execution-previous').disabled = executionPage <= 1;
        $('execution-next').disabled = executionPage >= pages;
        $('executions').replaceChildren();
        if (!executions.length) $('executions').append(node('p', t('暂无执行任务。提交任务后将显示在这里。'), 'muted'));
        else {
            const table = node('table', '', 'execution-table');
            const head = node('thead', ''), headings = node('tr', '');
            for (const title of ['ID', t('Name / 名称'), t('任务类型'), t('状态'), t('操作')]) headings.append(node('th', title));
            head.append(headings); table.append(head);
            const body = node('tbody', '');
            const states = { Enqueued: t('排队中'), Processing: t('运行中'), Scheduled: t('等待执行'), Succeeded: t('已成功'), Failed: t('失败'), Deleted: t('已删除'), Awaiting: t('等待前置任务'), Unknown: t('未知') };
            for (const execution of executions) {
                const row = node('tr', ''), idCell = node('td', ''), nameCell = node('td', ''), stateCell = node('td', ''), actionCell = node('td', '');
                const link = node('a', execution.id); link.href = `/hangfire/jobs/details/${encodeURIComponent(execution.id)}`; idCell.append(link);
                nameCell.append(node('strong', execution.name), node('div', execution.jobKind === 'WebAPI' ? execution.url : `${execution.pluginId}/${execution.jobId}`, 'hint'));
                stateCell.append(node('span', states[execution.state] || execution.state, `badge ${execution.state}`));
                if (canRun) {
                    const actions = node('div', '', 'actions');
                    for (const restart of [true, false]) {
                        const button = node('button', restart ? t('重启') : t('删除')); button.type = 'button';
                        button.disabled = restart ? !execution.canRestart : !execution.canDelete;
                        button.title = restart && !execution.canRestart ? t('运行中或已排队的任务不能重复启动') : '';
                        button.onclick = async () => {
                            const message = restart ? t("将任务 {0} 重新入队？参数和批次 ID 保持不变。", execution.id) : t("删除任务 {0}？运行中的任务会请求取消，已产生的业务结果不会撤销。", execution.id);
                            if (!confirm(message)) return;
                            button.disabled = true;
                            try {
                                await api(`/api/plugin-executions/${encodeURIComponent(execution.id)}${restart ? '/restart' : ''}`, restart ? 'POST' : 'DELETE', { expectedState: execution.state });
                                feedback(restart ? t("任务 {0} 已重新入队。", execution.id) : t("任务 {0} 已标记为删除。", execution.id));
                            } catch (e) { feedback(e.message, true); }
                            finally { await refresh().catch(e => feedback(e.message, true)); }
                        };
                        actions.append(button);
                    }
                    actionCell.append(actions);
                } else actionCell.append(node('span', t('只读'), 'muted'));
                const types = { 'Fire-and-forget': t('立即执行'), Delayed: t('延迟执行'), Recurring: t('周期任务'), Unknown: t('未知') };
                const typeCell = node('td', '');
                typeCell.append(node('span', types[execution.taskType] || t('未知'), 'badge'));
                if (execution.taskType && execution.taskType !== 'Unknown') typeCell.append(node('div', execution.taskType, 'hint'));
                row.append(idCell, nameCell, typeCell, stateCell, actionCell); body.append(row);
            }
            table.append(body); $('executions').append(table);
        }
    }
    if (canRun) {
        $('job-kind').addEventListener('change', kindChanged); kindChanged();
        $('mode').addEventListener('change', modeChanged); $('task').addEventListener('change', selectTask); modeChanged();
        $('job-form').addEventListener('submit', async event => {
            event.preventDefault(); $('submit-job').disabled = true;
            try {
                const name = $('job-name').value.trim();
                if (!name) throw new Error(t('请输入任务名。'));
                if ($('job-kind').value === 'WebAPI') {
                    const url = $('webapi-url').value.trim();
                    let parsed;
                    try { parsed = new URL(url); } catch { throw new Error(t('请输入有效的 HTTP 或 HTTPS URL。')); }
                    if (!['http:', 'https:'].includes(parsed.protocol) || parsed.username || parsed.password || parsed.hash)
                        throw new Error(t('请输入有效的 HTTP 或 HTTPS URL。'));
                    if ($('mode').value === 'Recurring') {
                        await api(`/api/webapi-schedules/${encodeURIComponent($('schedule-id').value)}`, 'PUT', { name, url, cron: $('cron').value, timeZone: $('timezone').value });
                        $('create-dialog').close(); feedback(t('周期计划已保存。'));
                    } else {
                        const result = await api('/api/webapi-jobs/executions', 'POST', { name, url, mode: $('mode').value, delayMinutes: $('mode').value === 'Delayed' ? Number($('delay').value) : null });
                        $('create-dialog').close(); feedback(t('任务已提交 · ID {0}', result.jobId), false, result.dashboardUrl);
                    }
                    await refresh();
                    return;
                }
                const [pluginId, jobId] = $('task').value.split('/'); if (!pluginId || !jobId) throw new Error(t('请选择已激活的任务。'));
                let parameters;
                try { parameters = JSON.parse($('parameters').value); } catch { throw new Error(t('JSON 格式不正确。')); }
                if (!parameters || typeof parameters !== 'object' || Array.isArray(parameters)) throw new Error(t('JSON 参数必须是对象。'));
                if ($('mode').value === 'Recurring') {
                    await api(`/api/plugin-schedules/${encodeURIComponent($('schedule-id').value)}`, 'PUT', { name, pluginId, jobId, parameters, cron: $('cron').value, timeZone: $('timezone').value });
                    $('create-dialog').close(); feedback(t('周期计划已保存。')); await refresh();
                } else {
                    const result = await api(`/api/plugin-jobs/${pluginId}/${jobId}/executions`, 'POST', { name, mode: $('mode').value, parameters, delayMinutes: $('mode').value === 'Delayed' ? Number($('delay').value) : null });
                    $('create-dialog').close(); feedback(t("任务已提交 · ID {0}", result.jobId), false, result.dashboardUrl);
                    await refresh();
                }
            } catch (e) { feedback(e.message, true); } finally { $('submit-job').disabled = false; }
        });
    }
    async function poll() { try { await refresh(); } catch (e) { feedback(e.message, true); } finally { setTimeout(poll, 5000); } }
    $('execution-previous').addEventListener('click', () => { executionPage--; renderExecutions(); });
    $('execution-next').addEventListener('click', () => { executionPage++; renderExecutions(); });
    $('execution-page-size').addEventListener('change', () => {
        const selectedSize = Number($('execution-page-size').value);
        executionPageSize = [20, 50, 100].includes(selectedSize) ? selectedSize : 20;
        $('execution-page-size').value = String(executionPageSize);
        executionPage = 1;
        renderExecutions();
    });
    window.addEventListener('languagechange', () => { if (canRun) modeChanged(); refresh().catch(e => feedback(e.message, true)); });
    poll();
})();
