(() => {
    const english = {
        '任务名': 'Job name', '请输入任务名。': 'Enter a job name.',
        '插件任务': 'Plugin job', 'WebAPI 任务': 'WebAPI job',
        'Worker 将发送 GET 请求，无需填写 JSON 参数。': 'The Worker sends a GET request. No JSON parameters are needed.',
        '请输入有效的 HTTP 或 HTTPS URL。': 'Enter a valid HTTP or HTTPS URL without credentials or a fragment.',
        '任务列表分页': 'Job pagination', '每页条数': 'Rows per page',
        '共 {0} 个任务 · 第 {1} / {2} 页': '{0} jobs · Page {1} of {2}',
        '任务管理': 'Job manager', '插件库': 'Plugin library', '创建任务': 'Create job',
        '上传功能，选择时间，让 Worker 完成执行。': 'Choose a plugin and schedule a job for the Worker to execute.',
        '任务列表': 'Jobs', '最近 100 条 · 每 5 秒刷新': 'Latest 100 jobs · Refreshes every 5 seconds',
        '重启会将原任务重新入队，保留参数和批次 ID；业务去重规则仍然生效。删除不影响周期计划，也不会撤销已完成的业务操作。': 'Restart requeues the same job with its parameters and batch ID. Business deduplication still applies. Deleting a job does not remove its recurring schedule or undo completed business operations.',
        '正在读取任务…': 'Loading jobs…', '关闭': 'Close', '关闭创建任务': 'Close job creation',
        '尚未开始的任务和重试自动使用最新激活版本；正在执行的任务保持当前版本。': 'Queued jobs and retries use the latest active version. Running jobs keep their current version.',
        '功能': 'Function', '暂无可用插件任务': 'No plugin jobs available', '执行方式': 'Schedule type',
        '延迟分钟': 'Delay (minutes)', '计划 ID': 'Schedule ID', 'Cron（五段）': 'Cron (5 fields)', '时区': 'Time zone',
        '0 9 * * * 表示每天 09:00；* * * * * 表示每分钟。': '0 9 * * * runs daily at 09:00; * * * * * runs every minute.',
        'JSON 参数': 'JSON parameters', '提交任务': 'Submit job', '保存周期计划': 'Save recurring schedule',
        '当前为只读账户，可以查看插件和周期计划。': 'This account has read-only access.',
        'API 管理调度 · Worker 执行插件 · SQL Server 持久化': 'API scheduling · Worker execution · SQL Server storage',
        '管理插件版本、查看加载状态和上传新功能。': 'Manage plugin versions, check loading status and upload new functions.',
        '返回任务管理': 'Back to jobs', '插件列表': 'Plugins', '每 5 秒刷新': 'Refreshes every 5 seconds',
        '添加插件': 'Add plugin', '搜索插件': 'Search plugins', '插件 ID、功能名称或版本': 'Plugin ID, function name or version',
        '状态': 'Status', '全部状态': 'All statuses', '已激活': 'Active', '待加载': 'Pending', '加载失败': 'Load failed', '历史版本': 'Superseded',
        '正在读取插件…': 'Loading plugins…', '插件列表分页': 'Plugin pagination', '上一页': 'Previous', '下一页': 'Next',
        '插件 ZIP 包': 'Plugin ZIP package', '包含 plugin.json、DLL 和依赖，最大 50 MB。': 'Includes plugin.json, DLLs and dependencies. Maximum 50 MB.',
        '上传并加载': 'Upload and load', '查看执行详情 ↗': 'View job details ↗',
        '暂无执行任务。提交任务后将显示在这里。': 'No jobs yet. Submitted jobs will appear here.',
        'Name / 名称': 'Name', '任务类型': 'Job type', '操作': 'Actions', '排队中': 'Enqueued', '运行中': 'Processing',
        '等待执行': 'Scheduled', '已成功': 'Succeeded', '失败': 'Failed', '已删除': 'Deleted', '等待前置任务': 'Awaiting', '未知': 'Unknown',
        '重启': 'Restart', '删除': 'Delete', '运行中或已排队的任务不能重复启动': 'Running or queued jobs cannot be restarted.',
        '只读': 'Read only', '立即执行': 'Fire-and-forget', '延迟执行': 'Delayed', '周期任务': 'Recurring',
        '请选择已激活的任务。': 'Select an active plugin job.', 'JSON 参数必须是对象。': 'JSON parameters must be an object.',
        '周期计划已保存。': 'Recurring schedule saved.', '还没有插件。上传示例 ZIP 开始。': 'No plugins yet. Upload a plugin ZIP to get started.',
        '没有符合筛选条件的插件。': 'No plugins match your filters.', '插件 / 功能': 'Plugin / Function', '版本': 'Version',
        'Worker 已加载': 'Loaded by Worker', '暂无在线 Worker 加载确认': 'No online Worker has confirmed loading',
        '请求失败 ({0})': 'Request failed ({0})',
        '将任务 {0} 重新入队？参数和批次 ID 保持不变。': 'Requeue job {0}? Parameters and batch ID will stay the same.',
        '删除任务 {0}？运行中的任务会请求取消，已产生的业务结果不会撤销。': 'Delete job {0}? Running jobs will be asked to cancel. Completed business operations will not be undone.',
        '任务 {0} 已重新入队。': 'Job {0} has been requeued.', '任务 {0} 已标记为删除。': 'Job {0} has been marked as deleted.',
        '任务已提交 · ID {0}': 'Job submitted · ID {0}',
        '共 {0} 个版本 · 第 {1} / {2} 页 · 每页 20 条': '{0} versions · Page {1} of {2} · 20 per page',
        '删除插件 {0} / {1}？删除激活版本后不再提供该插件任务，也不会自动切回旧版本。正在执行的任务继续完成；尚未开始的任务和周期执行在没有激活版本时会失败。DLL 文件保留在磁盘。': 'Delete plugin {0} / {1}? Deleting the active version makes its jobs unavailable; no older version is activated automatically. Running jobs continue. Queued and recurring executions fail without an active version. DLL files remain on disk.',
        '插件 {0} / {1} 已删除。': 'Plugin {0} / {1} deleted.',
        '{0} {1} 上传成功，加载状态请查看列表。': '{0} {1} uploaded. Check its loading status in the list.',
        'JSON 格式不正确。': 'Invalid JSON syntax.'
    };
    let language = 'en';
    try { if (localStorage.getItem('hangfire-language') === 'zh-CN') language = 'zh-CN'; } catch { /* Storage may be disabled. */ }
    const t = (key, ...args) => (language === 'en' ? english[key] || key : key)
        .replace(/\{(\d+)\}/g, (match, index) => index < args.length ? String(args[index]) : match);
    function apply() {
        document.documentElement.lang = language;
        document.querySelectorAll('[data-i18n]').forEach(el => { el.textContent = t(el.dataset.i18n); });
        for (const attribute of ['placeholder', 'aria-label']) {
            document.querySelectorAll(`[data-i18n-${attribute}]`).forEach(el => el.setAttribute(attribute, t(el.getAttribute(`data-i18n-${attribute}`))));
        }
        document.title = t(document.body.dataset.pageTitle) + ' · Hangfire';
        document.querySelectorAll('[data-language-picker]').forEach(select => { select.value = language; });
    }
    const errors = {
        'Enter an absolute HTTP or HTTPS URL without credentials or a fragment.': '请输入完整的 HTTP 或 HTTPS URL，不能包含用户名、密码或片段。',
        'This plugin version already exists. Upload a new version.': '此插件版本已存在，请上传新版本。',
        'Select a non-empty ZIP plugin package.': '请选择非空的插件 ZIP 包。',
        'No active plugin task matches this ID.': '找不到对应的已激活插件任务。',
        'Plugin execution not found.': '找不到此任务。',
        'Plugin version not found.': '找不到此插件版本。',
        'Task state changed. Refresh the list and try again.': '任务状态已变化，请刷新列表后重试。',
        'A queued or running task cannot be restarted.': '排队中或运行中的任务不能重启。',
        'Use a five-field Cron expression.': '请使用五段 Cron 表达式。',
        'Time zone is required.': '请填写时区。',
        'Choose Fire-and-forget without delay, or Delayed with 1–1440 minutes.': '立即执行无需设置延迟；延迟执行请设置 1–1440 分钟。',
        'Schedule not found.': '找不到周期计划。'
    };
    window.ui = { t, apply, error: message => language === 'zh-CN' ? errors[message] || message : message };
    apply();
    document.querySelectorAll('[data-language-picker]').forEach(select => select.addEventListener('change', () => {
        language = select.value === 'zh-CN' ? 'zh-CN' : 'en';
        try { localStorage.setItem('hangfire-language', language); } catch { /* Keep this page usable. */ }
        apply();
        document.querySelectorAll('#feedback,.dialog-feedback').forEach(el => { el.hidden = true; });
        window.dispatchEvent(new Event('languagechange'));
    }));
})();
