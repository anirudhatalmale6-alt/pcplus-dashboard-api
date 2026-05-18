// PC Plus Endpoint Protection - Dashboard
const API = '/api/dashboard';
const ENDPOINT_API = '/api/endpoint';

// Auth-aware fetch wrapper - redirects to login on 401
async function apiFetch(url, options) {
    const res = await fetch(url, options);
    if (res.status === 401) { window.location.href = '/login.html'; return null; }
    return res;
}

function logout() {
    fetch('/api/auth/logout', { method: 'POST' }).then(() => window.location.href = '/login.html');
}

// --- Mobile sidebar toggle ---
function toggleSidebar() {
    const sidebar = document.getElementById('sidebar');
    const overlay = document.getElementById('sidebar-overlay');
    sidebar.classList.toggle('open');
    overlay.classList.toggle('active');
}

// Close sidebar when nav item clicked on mobile
document.addEventListener('DOMContentLoaded', () => {
    document.querySelectorAll('.nav-item').forEach(item => {
        item.addEventListener('click', () => {
            if (window.innerWidth <= 768) {
                const sidebar = document.getElementById('sidebar');
                const overlay = document.getElementById('sidebar-overlay');
                sidebar.classList.remove('open');
                overlay.classList.remove('active');
            }
        });
    });
});

let currentPage = 'overview';
let refreshInterval;
let allDevices = [];
let currentUserRole = '';
let currentUserCustomer = '';

// Load user info and configure portal mode
(async function initUserContext() {
    try {
        const res = await fetch('/api/auth/me');
        if (res.ok) {
            const me = await res.json();
            currentUserRole = me.role || '';
            currentUserCustomer = me.customerName || '';
            if (currentUserRole === 'customer' && currentUserCustomer) {
                // Customer portal mode: hide admin nav items, auto-filter to customer
                document.querySelectorAll('.nav-section').forEach(s => {
                    if (['Settings', 'Analytics'].includes(s.textContent.trim())) s.style.display = 'none';
                });
                // Hide settings nav items
                const hiddenPages = ['config', 'email-reports', 'notifications', 'users', 'branding', 'trends'];
                document.querySelectorAll('.nav-item').forEach(item => {
                    const span = item.querySelector('span');
                    if (span && ['Config Push', 'Email Reports', 'Notifications', 'Users', 'Branding', 'Trends'].includes(span.textContent.trim())) {
                        item.style.display = 'none';
                    }
                });
                // Redirect to customer dashboard
                showCustomerDashboard(currentUserCustomer);
            }
        }
    } catch {}
})();

// --- Navigation ---
function showPage(page) {
    document.querySelectorAll('.page').forEach(p => p.style.display = 'none');
    document.querySelectorAll('.nav-item').forEach(n => n.classList.remove('active'));

    document.getElementById('page-' + page).style.display = 'block';
    document.querySelectorAll('.nav-item')[
        ['overview', 'devices', 'alerts', 'incidents', 'policies', 'trends', 'config', 'email-reports', 'notifications', 'users', 'branding']
            .indexOf(page)
    ]?.classList.add('active');

    currentPage = page;
    refreshCurrentPage();
}

let currentCustomer = '';

function refreshCurrentPage() {
    switch (currentPage) {
        case 'overview': loadOverview(); break;
        case 'customer': loadCustomerDashboard(currentCustomer); break;
        case 'device': loadDeviceDetail(); break;
        case 'devices': loadDevices(); break;
        case 'alerts': loadAlerts(); break;
        case 'incidents': loadIncidents(); break;
        case 'policies': loadPolicies(); break;
        case 'config': loadConfigPage(); break;
        case 'email-reports': loadEmailReports(); break;
        case 'trends': loadTrends(); break;
        case 'notifications': loadNotifications(); break;
        case 'users': loadUsers(); break;
        case 'branding': loadBranding(); break;
    }
}

// --- SVG Donut Chart Helper ---
function createDonutChart(containerId, segments, centerText, size = 140) {
    const el = document.getElementById(containerId);
    if (!el) return;
    const total = segments.reduce((s, seg) => s + seg.value, 0);
    if (total === 0) {
        el.innerHTML = `<div style="color:var(--text-muted);font-size:13px">No data</div>`;
        return;
    }
    const r = size / 2 - 10;
    const cx = size / 2, cy = size / 2;
    const strokeWidth = 18;
    const circumference = 2 * Math.PI * r;
    let offset = 0;

    let arcs = '';
    let legend = '';
    segments.forEach(seg => {
        if (seg.value === 0) return;
        const pct = seg.value / total;
        const dash = pct * circumference;
        const gap = circumference - dash;
        arcs += `<circle cx="${cx}" cy="${cy}" r="${r}" fill="none" stroke="${seg.color}" stroke-width="${strokeWidth}" stroke-dasharray="${dash} ${gap}" stroke-dashoffset="${-offset}" transform="rotate(-90 ${cx} ${cy})"/>`;
        offset += dash;
        legend += `<div style="display:flex;align-items:center;gap:6px;font-size:11px;margin:2px 0"><span style="width:10px;height:10px;border-radius:2px;background:${seg.color};flex-shrink:0"></span><span style="color:var(--text-muted)">${seg.label}</span><span style="margin-left:auto;color:var(--text-primary);font-weight:600">${seg.value}</span></div>`;
    });

    el.innerHTML = `
        <svg width="${size}" height="${size}" viewBox="0 0 ${size} ${size}">
            <circle cx="${cx}" cy="${cy}" r="${r}" fill="none" stroke="rgba(255,255,255,0.05)" stroke-width="${strokeWidth}"/>
            ${arcs}
            <text x="${cx}" y="${cy - 4}" text-anchor="middle" fill="var(--text-primary)" font-size="24" font-weight="bold">${centerText}</text>
            <text x="${cx}" y="${cy + 14}" text-anchor="middle" fill="var(--text-muted)" font-size="10">total</text>
        </svg>
        <div style="margin-top:8px">${legend}</div>
    `;
}

// --- Canvas Line Chart Helper ---
function drawAlertTimeline(canvasId, data) {
    const canvas = document.getElementById(canvasId);
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    const dpr = window.devicePixelRatio || 1;
    const rect = canvas.parentElement.getBoundingClientRect();
    canvas.width = (rect.width - 40) * dpr;
    canvas.height = 200 * dpr;
    canvas.style.width = (rect.width - 40) + 'px';
    canvas.style.height = '200px';
    ctx.scale(dpr, dpr);

    const w = rect.width - 40, h = 200;
    const padding = { top: 10, right: 10, bottom: 30, left: 40 };
    const plotW = w - padding.left - padding.right;
    const plotH = h - padding.top - padding.bottom;

    ctx.clearRect(0, 0, w, h);

    if (!data || data.length === 0) {
        ctx.fillStyle = '#6b7280';
        ctx.font = '13px Segoe UI';
        ctx.textAlign = 'center';
        ctx.fillText('No alert data yet', w / 2, h / 2);
        return;
    }

    const maxVal = Math.max(...data.map(d => d.count), 1);

    // Grid lines
    ctx.strokeStyle = 'rgba(255,255,255,0.06)';
    ctx.lineWidth = 1;
    for (let i = 0; i <= 4; i++) {
        const y = padding.top + (plotH / 4) * i;
        ctx.beginPath(); ctx.moveTo(padding.left, y); ctx.lineTo(w - padding.right, y); ctx.stroke();
        ctx.fillStyle = '#6b7280'; ctx.font = '10px Segoe UI'; ctx.textAlign = 'right';
        ctx.fillText(Math.round(maxVal - (maxVal / 4) * i), padding.left - 6, y + 4);
    }

    // Line
    ctx.beginPath();
    ctx.strokeStyle = '#ef4444';
    ctx.lineWidth = 2;
    ctx.lineJoin = 'round';
    data.forEach((d, i) => {
        const x = padding.left + (i / Math.max(data.length - 1, 1)) * plotW;
        const y = padding.top + plotH - (d.count / maxVal) * plotH;
        if (i === 0) ctx.moveTo(x, y); else ctx.lineTo(x, y);
    });
    ctx.stroke();

    // Fill under line
    const lastIdx = data.length - 1;
    ctx.lineTo(padding.left + plotW, padding.top + plotH);
    ctx.lineTo(padding.left, padding.top + plotH);
    ctx.closePath();
    const gradient = ctx.createLinearGradient(0, padding.top, 0, padding.top + plotH);
    gradient.addColorStop(0, 'rgba(239, 68, 68, 0.15)');
    gradient.addColorStop(1, 'rgba(239, 68, 68, 0)');
    ctx.fillStyle = gradient;
    ctx.fill();

    // X labels
    ctx.fillStyle = '#6b7280'; ctx.font = '10px Segoe UI'; ctx.textAlign = 'center';
    const step = Math.max(1, Math.floor(data.length / 5));
    data.forEach((d, i) => {
        if (i % step === 0 || i === lastIdx) {
            const x = padding.left + (i / Math.max(data.length - 1, 1)) * plotW;
            ctx.fillText(d.label, x, h - 8);
        }
    });
}

// --- Overview ---
async function loadOverview() {
    try {
        const [overview, alerts, devices] = await Promise.all([
            fetch(API + '/overview').then(r => r.json()),
            fetch(API + '/alerts?limit=50&acknowledged=false').then(r => r.json()),
            fetch(API + '/devices').then(r => r.json())
        ]);

        // Top stat cards
        const statsEl = document.getElementById('overview-stats');
        statsEl.innerHTML = `
            <div class="stat-card blue">
                <div class="label">Total Devices</div>
                <div class="value">${overview.totalDevices}</div>
                <div class="sub">${overview.onlineDevices} online, ${overview.offlineDevices} offline</div>
            </div>
            <div class="stat-card ${overview.criticalAlerts > 0 ? 'red' : 'green'}">
                <div class="label">Active Alerts</div>
                <div class="value">${overview.activeAlerts}</div>
                <div class="sub">${overview.criticalAlerts} critical</div>
            </div>
            <div class="stat-card ${overview.devicesInLockdown > 0 ? 'red' : 'green'}">
                <div class="label">Lockdowns</div>
                <div class="value">${overview.devicesInLockdown}</div>
                <div class="sub">${overview.devicesInLockdown > 0 ? 'Active lockdowns!' : 'All clear'}</div>
            </div>
            <div class="stat-card ${overview.avgSecurityScore >= 80 ? 'green' : overview.avgSecurityScore >= 60 ? 'yellow' : 'red'}">
                <div class="label">Avg Security Score</div>
                <div class="value">${Math.round(overview.avgSecurityScore)}</div>
                <div class="sub">Across all devices</div>
            </div>
            <div class="stat-card ${overview.openIncidents > 0 ? 'orange' : 'green'}">
                <div class="label">Open Incidents</div>
                <div class="value">${overview.openIncidents}</div>
            </div>
            <div class="stat-card purple">
                <div class="label">Protected Endpoints</div>
                <div class="value">${devices.filter(d => d.isOnline && d.runningModules > 0).length}</div>
                <div class="sub">of ${overview.totalDevices} total</div>
            </div>
        `;

        // Alerts by category donut
        const categories = {};
        alerts.forEach(a => {
            const cat = a.category || a.moduleId || 'Other';
            categories[cat] = (categories[cat] || 0) + 1;
        });
        const catColors = ['#3b82f6', '#ef4444', '#f59e0b', '#22c55e', '#8b5cf6', '#ec4899', '#06b6d4'];
        const catSegments = Object.entries(categories).map(([label, value], i) => ({
            label: label.charAt(0).toUpperCase() + label.slice(1),
            value,
            color: catColors[i % catColors.length]
        }));
        createDonutChart('alerts-donut', catSegments, alerts.length.toString());

        // Alerts over time (group by day for last 7 days)
        const now = new Date();
        const timelineData = [];
        for (let i = 6; i >= 0; i--) {
            const d = new Date(now); d.setDate(d.getDate() - i);
            const dayStr = d.toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
            const dayStart = new Date(d.getFullYear(), d.getMonth(), d.getDate());
            const dayEnd = new Date(dayStart); dayEnd.setDate(dayEnd.getDate() + 1);
            const count = alerts.filter(a => {
                const t = new Date(a.timestamp);
                return t >= dayStart && t < dayEnd;
            }).length;
            timelineData.push({ label: dayStr, count });
        }
        drawAlertTimeline('alerts-timeline', timelineData);

        // Customers table - group devices by customer
        allDevices = devices;
        const threatsTable = document.getElementById('security-threats-table');
        if (devices.length === 0) {
            threatsTable.innerHTML = '<tr><td colspan="5" style="text-align:center;padding:20px;color:var(--text-muted)">No devices</td></tr>';
        } else {
            // Group by customer
            const customers = {};
            devices.forEach(d => {
                const cust = d.customerName || d.customerId || 'Unassigned';
                if (!customers[cust]) customers[cust] = [];
                customers[cust].push(d);
            });

            threatsTable.innerHTML = Object.entries(customers).map(([custName, custDevices]) => {
                const onlineCount = custDevices.filter(d => d.isOnline).length;
                const avgScore = Math.round(custDevices.reduce((s, d) => s + (d.securityScore || 0), 0) / custDevices.length);
                const custAlerts = custDevices.reduce((s, d) => {
                    return s + alerts.filter(a => (a.hostname || a.deviceId) === (d.hostname || d.deviceId)).length;
                }, 0);
                const scoreClass = avgScore >= 80 ? 'b' : avgScore >= 60 ? 'c' : 'd';

                return `<tr style="cursor:pointer" onclick="showCustomerDashboard('${esc(custName)}')">
                    <td style="font-weight:600;color:#3b82f6;text-decoration:underline">${esc(custName)}</td>
                    <td>${custDevices.length}</td>
                    <td><span class="score ${scoreClass}">${avgScore >= 80 ? 'B' : avgScore >= 60 ? 'C' : 'D'}</span> <span style="font-size:11px;color:var(--text-muted)">${avgScore}/100</span></td>
                    <td>${custAlerts > 0 ? `<span style="color:#ef4444;font-weight:600">${custAlerts}</span>` : '<span style="color:var(--text-muted)">0</span>'}</td>
                    <td><span style="color:#22c55e;font-weight:500">${onlineCount}</span><span style="color:var(--text-muted)">/${custDevices.length}</span></td>
                </tr>`;
            }).join('');
        }

        // Protection status donuts
        const onlineCount = devices.filter(d => d.isOnline).length;
        const offlineCount = devices.filter(d => !d.isOnline).length;
        const lockdownCount = devices.filter(d => d.lockdownActive).length;
        const protectedCount = devices.filter(d => d.runningModules > 0 && d.isOnline).length;
        const unprotectedCount = devices.filter(d => d.runningModules === 0 && d.isOnline).length;
        const goodScore = devices.filter(d => d.securityScore >= 80).length;
        const medScore = devices.filter(d => d.securityScore >= 50 && d.securityScore < 80).length;
        const lowScore = devices.filter(d => d.securityScore < 50).length;

        createDonutChart('donut-ransomware', [
            { label: 'Active', value: protectedCount, color: '#22c55e' },
            { label: 'Inactive', value: unprotectedCount, color: '#6b7280' },
            { label: 'Lockdown', value: lockdownCount, color: '#ef4444' }
        ], devices.length.toString(), 120);

        createDonutChart('donut-defender', [
            { label: 'Protected', value: protectedCount, color: '#22c55e' },
            { label: 'Unprotected', value: unprotectedCount + offlineCount, color: '#6b7280' }
        ], protectedCount.toString(), 120);

        createDonutChart('donut-security', [
            { label: 'Good (80+)', value: goodScore, color: '#22c55e' },
            { label: 'Medium (50-79)', value: medScore, color: '#f59e0b' },
            { label: 'Low (<50)', value: lowScore, color: '#ef4444' }
        ], Math.round(overview.avgSecurityScore).toString(), 120);

        createDonutChart('donut-status', [
            { label: 'Online', value: onlineCount, color: '#22c55e' },
            { label: 'Offline', value: offlineCount, color: '#6b7280' },
            { label: 'Lockdown', value: lockdownCount, color: '#ef4444' }
        ], onlineCount.toString(), 120);

        // Recent alerts feed
        const alertsEl = document.getElementById('overview-alerts');
        if (alerts.length === 0) {
            alertsEl.innerHTML = '<div class="empty-state"><p>No active alerts. All systems healthy.</p></div>';
        } else {
            alertsEl.innerHTML = alerts.slice(0, 10).map(a => `
                <div class="alert-item">
                    <div class="alert-severity ${a.severity}"></div>
                    <div class="alert-content">
                        <div class="alert-title">${esc(a.title)}</div>
                        <div class="alert-message">${esc(a.message)}</div>
                        <div class="alert-meta">${esc(a.hostname || a.deviceId)} &middot; ${timeAgo(a.timestamp)} &middot; ${a.severity}</div>
                    </div>
                    <button class="btn btn-sm btn-secondary" onclick="ackAlert(${a.id})">Ack</button>
                </div>
            `).join('');
        }
    } catch (err) {
        console.error('Failed to load overview:', err);
    }
}

// --- Customer Dashboard ---
function showCustomerDashboard(customerName) {
    currentCustomer = customerName;
    // Show customer page
    document.querySelectorAll('.page').forEach(p => p.style.display = 'none');
    document.querySelectorAll('.nav-item').forEach(n => n.classList.remove('active'));
    document.getElementById('page-customer').style.display = 'block';
    currentPage = 'customer';
    loadCustomerDashboard(customerName);
}

async function loadCustomerDashboard(customerName) {
    if (!customerName) return;
    try {
        const [devices, alerts] = await Promise.all([
            fetch(API + '/devices').then(r => r.json()),
            fetch(API + '/alerts?limit=100&acknowledged=false').then(r => r.json())
        ]);

        allDevices = devices;
        const custDevices = devices.filter(d => (d.customerName || d.customerId || 'Unassigned') === customerName);
        const custAlerts = alerts.filter(a => custDevices.some(d => (d.hostname || d.deviceId) === (a.hostname || a.deviceId)));

        // Title
        document.getElementById('customer-title').textContent = customerName;

        // Stats
        const onlineCount = custDevices.filter(d => d.isOnline).length;
        const offlineCount = custDevices.filter(d => !d.isOnline).length;
        const avgScore = custDevices.length > 0 ? Math.round(custDevices.reduce((s, d) => s + (d.securityScore || 0), 0) / custDevices.length) : 0;
        const critAlerts = custAlerts.filter(a => a.severity === 'Critical' || a.severity === 'Emergency').length;
        const protectedCount = custDevices.filter(d => d.runningModules > 0 && d.isOnline).length;
        const avgCpu = custDevices.length > 0 ? Math.round(custDevices.reduce((s, d) => s + (d.cpuPercent || 0), 0) / custDevices.length) : 0;

        document.getElementById('customer-stats').innerHTML = `
            <div class="stat-card blue">
                <div class="label">Total Endpoints</div>
                <div class="value">${custDevices.length}</div>
                <div class="sub">${onlineCount} online, ${offlineCount} offline</div>
            </div>
            <div class="stat-card ${custAlerts.length > 0 ? 'red' : 'green'}">
                <div class="label">Active Alerts</div>
                <div class="value">${custAlerts.length}</div>
                <div class="sub">${critAlerts} critical</div>
            </div>
            <div class="stat-card ${avgScore >= 80 ? 'green' : avgScore >= 60 ? 'yellow' : 'red'}">
                <div class="label">Avg Security Score</div>
                <div class="value">${avgScore}</div>
                <div class="sub">Across ${custDevices.length} devices</div>
            </div>
            <div class="stat-card ${avgCpu > 80 ? 'red' : avgCpu > 60 ? 'yellow' : 'green'}">
                <div class="label">Avg CPU Usage</div>
                <div class="value">${avgCpu}%</div>
                <div class="sub">${protectedCount} protected endpoints</div>
            </div>
        `;

        // Donuts
        createDonutChart('cust-donut-status', [
            { label: 'Online', value: onlineCount, color: '#22c55e' },
            { label: 'Offline', value: offlineCount, color: '#6b7280' }
        ], custDevices.length.toString(), 120);

        const goodScore = custDevices.filter(d => d.securityScore >= 80).length;
        const medScore = custDevices.filter(d => d.securityScore >= 50 && d.securityScore < 80).length;
        const lowScore = custDevices.filter(d => d.securityScore < 50).length;
        createDonutChart('cust-donut-security', [
            { label: 'Good (80+)', value: goodScore, color: '#22c55e' },
            { label: 'Medium', value: medScore, color: '#f59e0b' },
            { label: 'Low (<50)', value: lowScore, color: '#ef4444' }
        ], avgScore.toString(), 120);

        const unprotected = custDevices.filter(d => d.runningModules === 0).length;
        createDonutChart('cust-donut-protection', [
            { label: 'Protected', value: protectedCount, color: '#22c55e' },
            { label: 'Unprotected', value: unprotected, color: '#ef4444' }
        ], protectedCount.toString(), 120);

        const alertCats = {};
        custAlerts.forEach(a => {
            const cat = a.category || a.moduleId || 'Other';
            alertCats[cat] = (alertCats[cat] || 0) + 1;
        });
        const catColors = ['#3b82f6', '#ef4444', '#f59e0b', '#22c55e', '#8b5cf6'];
        createDonutChart('cust-donut-alerts',
            Object.entries(alertCats).map(([label, value], i) => ({
                label: label.charAt(0).toUpperCase() + label.slice(1), value, color: catColors[i % catColors.length]
            })),
            custAlerts.length.toString(), 120);

        // Devices table
        const tbody = document.getElementById('customer-devices-table');
        tbody.innerHTML = custDevices.map(d => {
            const status = d.lockdownActive ? 'lockdown' : (d.isOnline ? 'online' : 'offline');
            const statusLabel = d.lockdownActive ? 'LOCKDOWN' : (d.isOnline ? 'Online' : 'Offline');
            const gradeClass = (d.securityGrade || '?').toLowerCase();
            const cpuColor = d.cpuPercent > 90 ? 'red' : d.cpuPercent > 70 ? 'orange' : 'green';
            const ramColor = d.ramPercent > 90 ? 'red' : d.ramPercent > 70 ? 'orange' : 'green';
            const diskColor = d.diskPercent > 90 ? 'red' : d.diskPercent > 80 ? 'orange' : 'green';
            const cpuTempColor = d.cpuTempC > 85 ? 'red' : d.cpuTempC > 70 ? 'orange' : 'green';
            const gpuTempColor = d.gpuTempC > 85 ? 'red' : d.gpuTempC > 70 ? 'orange' : 'green';

            return `<tr style="cursor:pointer" onclick="showDeviceDetail('${d.deviceId}')">
                <td><span class="badge ${status}"><span class="badge-dot"></span>${statusLabel}</span></td>
                <td style="font-weight:500;color:#3b82f6">${esc(d.hostname || d.deviceId)}</td>
                <td><span class="score ${gradeClass}">${esc(d.securityGrade || '?')}</span> <span style="font-size:11px;color:var(--text-muted)">${d.securityScore}/100</span></td>
                <td><div class="progress"><div class="progress-bar ${cpuColor}" style="width:${d.cpuPercent}%"></div></div>${Math.round(d.cpuPercent)}%</td>
                <td><div class="progress"><div class="progress-bar ${ramColor}" style="width:${d.ramPercent}%"></div></div>${Math.round(d.ramPercent)}%</td>
                <td><div class="progress"><div class="progress-bar ${diskColor}" style="width:${d.diskPercent}%"></div></div>${Math.round(d.diskPercent)}%</td>
                <td style="color:${cpuTempColor === 'red' ? '#ef4444' : cpuTempColor === 'orange' ? '#f59e0b' : '#22c55e'};font-weight:500">${d.cpuTempC > 0 ? Math.round(d.cpuTempC) + '°C' : '-'}</td>
                <td style="color:${gpuTempColor === 'red' ? '#ef4444' : gpuTempColor === 'orange' ? '#f59e0b' : '#22c55e'};font-weight:500">${d.gpuTempC > 0 ? Math.round(d.gpuTempC) + '°C' : '-'}</td>
                <td>${d.runningModules}</td>
                <td style="font-size:12px;color:var(--text-muted)">${timeAgo(d.lastSeen)}</td>
                <td><button class="btn btn-sm btn-secondary" onclick="event.stopPropagation();showDeviceDetail('${d.deviceId}')">Detail</button></td>
            </tr>`;
        }).join('');

        // Customer alerts
        const custAlertsEl = document.getElementById('customer-alerts');
        if (custAlerts.length === 0) {
            custAlertsEl.innerHTML = '<div class="empty-state"><p>No active alerts for this customer.</p></div>';
        } else {
            custAlertsEl.innerHTML = custAlerts.slice(0, 15).map(a => `
                <div class="alert-item">
                    <div class="alert-severity ${a.severity}"></div>
                    <div class="alert-content">
                        <div class="alert-title">${esc(a.title)}</div>
                        <div class="alert-message">${esc(a.message)}</div>
                        <div class="alert-meta">${esc(a.hostname || a.deviceId)} &middot; ${timeAgo(a.timestamp)} &middot; ${a.severity}</div>
                    </div>
                    <button class="btn btn-sm btn-secondary" onclick="ackAlert(${a.id})">Ack</button>
                </div>
            `).join('');
        }
    } catch (err) {
        console.error('Failed to load customer dashboard:', err);
    }
}

// --- Devices ---
async function loadDevices() {
    try {
        allDevices = await fetch(API + '/devices').then(r => r.json());
        renderDevices(allDevices);
    } catch (err) {
        console.error('Failed to load devices:', err);
    }
}

function renderDevices(devices) {
    const tbody = document.getElementById('devices-table');
    if (devices.length === 0) {
        tbody.innerHTML = '<tr><td colspan="13" style="text-align:center;padding:32px;color:var(--text-muted)">No devices registered yet. Endpoints will appear here when they start phoning home.</td></tr>';
        return;
    }

    tbody.innerHTML = devices.map(d => {
        const status = d.lockdownActive ? 'lockdown' : (d.isOnline ? 'online' : 'offline');
        const statusLabel = d.lockdownActive ? 'LOCKDOWN' : (d.isOnline ? 'Online' : 'Offline');
        const gradeClass = (d.securityGrade || '?').toLowerCase();
        const cpuColor = d.cpuPercent > 90 ? 'red' : d.cpuPercent > 70 ? 'orange' : 'green';
        const ramColor = d.ramPercent > 90 ? 'red' : d.ramPercent > 70 ? 'orange' : 'green';
        const diskColor = d.diskPercent > 90 ? 'red' : d.diskPercent > 80 ? 'orange' : 'green';
        const cpuTempColor = d.cpuTempC > 85 ? 'red' : d.cpuTempC > 70 ? 'orange' : 'green';
        const gpuTempColor = d.gpuTempC > 85 ? 'red' : d.gpuTempC > 70 ? 'orange' : 'green';

        return `<tr>
            <td><span class="badge ${status}"><span class="badge-dot"></span>${statusLabel}</span></td>
            <td style="color:var(--text-primary);font-weight:500">${esc(d.hostname || d.deviceId)}</td>
            <td>${esc(d.customerName || d.customerId || '-')}</td>
            <td><span class="score ${gradeClass}">${esc(d.securityGrade || '?')}</span> <span style="font-size:12px;color:var(--text-muted)">${d.securityScore}/100</span></td>
            <td><div class="progress"><div class="progress-bar ${cpuColor}" style="width:${d.cpuPercent}%"></div></div>${Math.round(d.cpuPercent)}%</td>
            <td><div class="progress"><div class="progress-bar ${ramColor}" style="width:${d.ramPercent}%"></div></div>${Math.round(d.ramPercent)}%</td>
            <td><div class="progress"><div class="progress-bar ${diskColor}" style="width:${d.diskPercent}%"></div></div>${Math.round(d.diskPercent)}%</td>
            <td style="color:${cpuTempColor === 'red' ? '#ef4444' : cpuTempColor === 'orange' ? '#f59e0b' : '#22c55e'};font-weight:500">${d.cpuTempC > 0 ? Math.round(d.cpuTempC) + '°C' : '-'}</td>
            <td style="color:${gpuTempColor === 'red' ? '#ef4444' : gpuTempColor === 'orange' ? '#f59e0b' : '#22c55e'};font-weight:500">${d.gpuTempC > 0 ? Math.round(d.gpuTempC) + '°C' : '-'}</td>
            <td><span class="badge info">${esc(d.licenseTier)}</span></td>
            <td>${d.runningModules}</td>
            <td style="font-size:12px;color:var(--text-muted)">${timeAgo(d.lastSeen)}</td>
            <td style="white-space:nowrap">
                <button class="btn btn-sm btn-secondary" onclick="reassignDevice('${d.deviceId}','${esc(d.customerName||'')}')" style="font-size:11px;padding:2px 8px" title="Move to different customer">Move</button>
                <button class="btn btn-sm btn-secondary" onclick="showDeviceDetail('${d.deviceId}')" style="font-size:11px;padding:2px 8px">Detail</button>
            </td>
        </tr>`;
    }).join('');
}

function filterDevices() {
    const q = document.getElementById('device-search').value.toLowerCase();
    if (!q) { renderDevices(allDevices); return; }
    renderDevices(allDevices.filter(d =>
        (d.hostname || '').toLowerCase().includes(q) ||
        (d.customerName || '').toLowerCase().includes(q) ||
        (d.deviceId || '').toLowerCase().includes(q)
    ));
}

function showDeviceDetail(deviceId) {
    const d = allDevices.find(x => x.deviceId === deviceId);
    if (!d) return;

    // Show device page instead of modal
    currentDevice = deviceId;
    document.querySelectorAll('.page').forEach(p => p.style.display = 'none');
    document.querySelectorAll('.nav-item').forEach(n => n.classList.remove('active'));
    document.getElementById('page-device').style.display = 'block';
    currentPage = 'device';
    loadDeviceDetail(d);
}

let currentDevice = '';

function loadDeviceDetail(d) {
    if (!d && currentDevice) d = allDevices.find(x => x.deviceId === currentDevice);
    if (!d) return;

    const status = d.lockdownActive ? 'lockdown' : (d.isOnline ? 'online' : 'offline');
    const statusLabel = d.lockdownActive ? 'LOCKDOWN' : (d.isOnline ? 'Online' : 'Offline');
    const gradeClass = (d.securityGrade || '?').toLowerCase();
    const cpuColor = d.cpuPercent > 90 ? '#ef4444' : d.cpuPercent > 70 ? '#f59e0b' : '#22c55e';
    const ramColor = d.ramPercent > 90 ? '#ef4444' : d.ramPercent > 70 ? '#f59e0b' : '#22c55e';
    const diskColor = d.diskPercent > 90 ? '#ef4444' : d.diskPercent > 80 ? '#f59e0b' : '#22c55e';
    const cpuTempColor = d.cpuTempC > 85 ? '#ef4444' : d.cpuTempC > 70 ? '#f59e0b' : '#22c55e';
    const gpuTempColor = d.gpuTempC > 85 ? '#ef4444' : d.gpuTempC > 70 ? '#f59e0b' : '#22c55e';
    const scoreColor = d.securityScore >= 80 ? '#22c55e' : d.securityScore >= 60 ? '#f59e0b' : '#ef4444';
    const uptimeStr = d.uptime || '-';

    // Build circular gauge SVG helper
    function gauge(pct, color, size=80) {
        const r = (size/2) - 6;
        const circ = 2 * Math.PI * r;
        const dash = (pct/100) * circ;
        return `<svg width="${size}" height="${size}" viewBox="0 0 ${size} ${size}">
            <circle cx="${size/2}" cy="${size/2}" r="${r}" fill="none" stroke="rgba(255,255,255,0.08)" stroke-width="8"/>
            <circle cx="${size/2}" cy="${size/2}" r="${r}" fill="none" stroke="${color}" stroke-width="8"
                stroke-dasharray="${dash} ${circ - dash}" stroke-dashoffset="${circ/4}" stroke-linecap="round"
                transform="rotate(-90 ${size/2} ${size/2})"/>
            <text x="${size/2}" y="${size/2 + 5}" text-anchor="middle" fill="var(--text-primary)" font-size="18" font-weight="bold">${Math.round(pct)}%</text>
        </svg>`;
    }

    // Navigation breadcrumb
    const custName = d.customerName || d.customerId || 'Unassigned';
    document.getElementById('device-title').textContent = d.hostname || d.deviceId;
    document.getElementById('device-breadcrumb').innerHTML = `
        <a href="#" onclick="showPage('overview');return false" style="color:#3b82f6;text-decoration:none">Dashboard</a>
        <span style="color:var(--text-muted);margin:0 6px">/</span>
        <a href="#" onclick="showCustomerDashboard('${esc(custName)}');return false" style="color:#3b82f6;text-decoration:none">${esc(custName)}</a>
        <span style="color:var(--text-muted);margin:0 6px">/</span>
        <span style="color:var(--text-primary)">${esc(d.hostname || d.deviceId)}</span>
    `;

    const content = document.getElementById('device-detail-content');
    content.innerHTML = `
        <!-- Device Header -->
        <div class="card" style="padding:20px;margin-bottom:16px">
            <div style="display:flex;align-items:center;justify-content:space-between;flex-wrap:wrap;gap:12px">
                <div style="display:flex;align-items:center;gap:16px">
                    <div style="width:56px;height:56px;border-radius:12px;background:linear-gradient(135deg,#1e293b,#334155);display:flex;align-items:center;justify-content:center">
                        <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="#60a5fa" stroke-width="2"><rect x="2" y="3" width="20" height="14" rx="2"/><line x1="8" y1="21" x2="16" y2="21"/><line x1="12" y1="17" x2="12" y2="21"/></svg>
                    </div>
                    <div>
                        <h2 style="margin:0;font-size:22px;font-weight:700">${esc(d.hostname || d.deviceId)}</h2>
                        <div style="color:var(--text-muted);font-size:13px;margin-top:2px">
                            ${esc(custName)} &middot; ${esc(d.osVersion || 'Unknown OS')} &middot; Agent v${esc(d.agentVersion || '-')}
                        </div>
                    </div>
                </div>
                <div style="display:flex;align-items:center;gap:10px">
                    <span class="badge ${status}" style="font-size:13px;padding:8px 16px"><span class="badge-dot"></span>${statusLabel}</span>
                    <button class="btn btn-sm btn-primary" onclick="showRemoteCommands('${esc(d.deviceId)}','${esc(d.hostname)}')">Remote Commands</button>
                    <button class="btn btn-sm btn-primary" onclick="sendDeviceCommand('${esc(d.deviceId)}','rescan')">Quick Scan</button>
                    <button class="btn btn-sm btn-danger" onclick="if(confirm('Lock down ${esc(d.hostname)}?'))sendDeviceCommand('${esc(d.deviceId)}','lockdown')">Lockdown</button>
                </div>
            </div>
        </div>

        <!-- Performance Gauges Row -->
        <div style="display:grid;grid-template-columns:repeat(5, 1fr);gap:12px;margin-bottom:16px">
            <div class="card" style="padding:16px;text-align:center">
                <div style="font-size:11px;color:var(--text-muted);text-transform:uppercase;letter-spacing:1px;margin-bottom:8px">CPU Usage</div>
                ${gauge(d.cpuPercent || 0, cpuColor)}
                <div style="font-size:11px;color:var(--text-muted);margin-top:4px">${d.cpuModel || 'CPU'}</div>
            </div>
            <div class="card" style="padding:16px;text-align:center">
                <div style="font-size:11px;color:var(--text-muted);text-transform:uppercase;letter-spacing:1px;margin-bottom:8px">Memory</div>
                ${gauge(d.ramPercent || 0, ramColor)}
                <div style="font-size:11px;color:var(--text-muted);margin-top:4px">${d.ramUsedGB ? d.ramUsedGB.toFixed(1) + ' / ' + d.ramTotalGB.toFixed(1) + ' GB' : '-'}</div>
            </div>
            <div class="card" style="padding:16px;text-align:center">
                <div style="font-size:11px;color:var(--text-muted);text-transform:uppercase;letter-spacing:1px;margin-bottom:8px">Disk Usage</div>
                ${gauge(d.diskPercent || 0, diskColor)}
                <div style="font-size:11px;color:var(--text-muted);margin-top:4px">${d.diskFreeGB ? d.diskFreeGB.toFixed(0) + ' GB free' : '-'}</div>
            </div>
            <div class="card" style="padding:16px;text-align:center">
                <div style="font-size:11px;color:var(--text-muted);text-transform:uppercase;letter-spacing:1px;margin-bottom:8px">CPU Temp</div>
                <div style="font-size:42px;font-weight:700;color:${cpuTempColor};line-height:1.2">${d.cpuTempC > 0 ? Math.round(d.cpuTempC) + '°' : '-'}</div>
                <div style="font-size:11px;color:var(--text-muted);margin-top:8px">GPU: ${d.gpuTempC > 0 ? Math.round(d.gpuTempC) + '°C' : '-'}</div>
            </div>
            <div class="card" style="padding:16px;text-align:center">
                <div style="font-size:11px;color:var(--text-muted);text-transform:uppercase;letter-spacing:1px;margin-bottom:8px">Security Score</div>
                ${gauge(d.securityScore || 0, scoreColor)}
                <div style="margin-top:4px"><span class="score ${gradeClass}" style="font-size:14px;padding:4px 10px">${esc(d.securityGrade || '?')}</span></div>
            </div>
        </div>

        <!-- Middle Row: Security + Network + System -->
        <div style="display:grid;grid-template-columns:1fr 1fr 1fr;gap:12px;margin-bottom:16px">
            <!-- Security Details - from real scan data -->
            <div class="card" style="padding:20px">
                <h4 style="margin:0 0 16px;font-size:14px;display:flex;align-items:center;gap:8px">
                    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#22c55e" stroke-width="2"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/></svg>
                    Security Details
                </h4>
                <div style="display:grid;gap:10px;font-size:13px">
                    <div style="display:flex;justify-content:space-between;padding-bottom:8px;border-bottom:1px solid var(--border)">
                        <span style="color:var(--text-muted)">Security Score</span>
                        <span style="font-weight:600;color:${scoreColor}">${d.securityScore}/100</span>
                    </div>
                    <div style="display:flex;justify-content:space-between;padding-bottom:8px;border-bottom:1px solid var(--border)">
                        <span style="color:var(--text-muted)">Grade</span>
                        <span class="score ${gradeClass}" style="font-size:12px;padding:2px 8px">${esc(d.securityGrade || '?')}</span>
                    </div>
                    <div style="display:flex;justify-content:space-between;padding-bottom:8px;border-bottom:1px solid var(--border)">
                        <span style="color:var(--text-muted)">Running Modules</span>
                        <span style="font-weight:600">${d.runningModules}</span>
                    </div>
                    <div style="display:flex;justify-content:space-between;padding-bottom:8px;border-bottom:1px solid var(--border)">
                        <span style="color:var(--text-muted)">License Tier</span>
                        <span class="badge info">${esc(d.licenseTier || 'Free')}</span>
                    </div>
                    <div style="display:flex;justify-content:space-between;padding-bottom:8px;border-bottom:1px solid var(--border)">
                        <span style="color:var(--text-muted)">Policy Profile</span>
                        <span style="font-weight:500">${esc(d.policyProfile || 'default')}</span>
                    </div>
                    <div style="display:flex;justify-content:space-between">
                        <span style="color:var(--text-muted)">Lockdown</span>
                        <span style="color:${d.lockdownActive ? '#ef4444' : '#22c55e'};font-weight:600">${d.lockdownActive ? 'ACTIVE' : 'Inactive'}</span>
                    </div>
                </div>
            </div>

            <!-- Network Details -->
            <div class="card" style="padding:20px">
                <h4 style="margin:0 0 16px;font-size:14px;display:flex;align-items:center;gap:8px">
                    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#3b82f6" stroke-width="2"><circle cx="12" cy="12" r="10"/><line x1="2" y1="12" x2="22" y2="12"/><path d="M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10z"/></svg>
                    Network
                </h4>
                <div style="display:grid;gap:10px;font-size:13px">
                    <div style="display:flex;justify-content:space-between;padding-bottom:8px;border-bottom:1px solid var(--border)">
                        <span style="color:var(--text-muted)">Local IP</span>
                        <span style="font-weight:600;font-family:monospace">${esc(d.localIp || d.ipAddress || '-')}</span>
                    </div>
                    <div style="display:flex;justify-content:space-between;padding-bottom:8px;border-bottom:1px solid var(--border)">
                        <span style="color:var(--text-muted)">Public IP</span>
                        <span style="font-weight:600;font-family:monospace">${esc(d.publicIp || '-')}</span>
                    </div>
                    <div style="display:flex;justify-content:space-between;padding-bottom:8px;border-bottom:1px solid var(--border)">
                        <span style="color:var(--text-muted)">MAC Address</span>
                        <span style="font-weight:500;font-family:monospace;font-size:11px">${esc(d.macAddress || '-')}</span>
                    </div>
                    <div style="display:flex;justify-content:space-between;padding-bottom:8px;border-bottom:1px solid var(--border)">
                        <span style="color:var(--text-muted)">Network Up</span>
                        <span style="font-weight:500">${d.networkSentKBps ? d.networkSentKBps.toFixed(0) + ' KB/s' : '-'}</span>
                    </div>
                    <div style="display:flex;justify-content:space-between;padding-bottom:8px;border-bottom:1px solid var(--border)">
                        <span style="color:var(--text-muted)">Network Down</span>
                        <span style="font-weight:500">${d.networkRecvKBps ? d.networkRecvKBps.toFixed(0) + ' KB/s' : '-'}</span>
                    </div>
                    <div style="display:flex;justify-content:space-between;padding-bottom:8px;border-bottom:1px solid var(--border)">
                        <span style="color:var(--text-muted)">Firewall</span>
                        <span style="color:${d.firewallEnabled !== false ? '#22c55e' : '#ef4444'};font-weight:600">${d.firewallEnabled !== false ? 'Enabled' : 'Disabled'}</span>
                    </div>
                    <div style="display:flex;justify-content:space-between">
                        <span style="color:var(--text-muted)">Open Ports</span>
                        <span style="font-weight:500">${d.openPorts || '-'}</span>
                    </div>
                </div>
            </div>

            <!-- System Details -->
            <div class="card" style="padding:20px">
                <h4 style="margin:0 0 16px;font-size:14px;display:flex;align-items:center;gap:8px">
                    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#8b5cf6" stroke-width="2"><rect x="2" y="3" width="20" height="14" rx="2"/><line x1="8" y1="21" x2="16" y2="21"/><line x1="12" y1="17" x2="12" y2="21"/></svg>
                    System Info
                </h4>
                <div style="display:grid;gap:10px;font-size:13px">
                    <div style="display:flex;justify-content:space-between;padding-bottom:8px;border-bottom:1px solid var(--border)">
                        <span style="color:var(--text-muted)">OS Version</span>
                        <span style="font-weight:500;font-size:11px">${esc(d.osVersion || '-')}</span>
                    </div>
                    <div style="display:flex;justify-content:space-between;padding-bottom:8px;border-bottom:1px solid var(--border)">
                        <span style="color:var(--text-muted)">Agent Version</span>
                        <span style="font-weight:600">v${esc(d.agentVersion || '-')}</span>
                    </div>
                    <div style="display:flex;justify-content:space-between;align-items:center;padding-bottom:8px;border-bottom:1px solid var(--border)">
                        <span style="color:var(--text-muted)">Customer</span>
                        <span style="display:flex;align-items:center;gap:8px">
                            <span style="font-weight:500" id="dev-customer-label">${esc(custName)}</span>
                            <button onclick="reassignDevice('${esc(d.deviceId)}','${esc(custName)}')" style="background:none;border:1px solid var(--border);color:var(--text-muted);padding:2px 8px;border-radius:4px;cursor:pointer;font-size:11px" title="Move to different customer">Move</button>
                        </span>
                    </div>
                    <div style="display:flex;justify-content:space-between;padding-bottom:8px;border-bottom:1px solid var(--border)">
                        <span style="color:var(--text-muted)">Device ID</span>
                        <span style="font-weight:500;font-family:monospace;font-size:10px">${esc(d.deviceId.substring(0,16))}...</span>
                    </div>
                    <div style="display:flex;justify-content:space-between;padding-bottom:8px;border-bottom:1px solid var(--border)">
                        <span style="color:var(--text-muted)">Registered</span>
                        <span style="font-weight:500">${d.registeredAt ? new Date(d.registeredAt).toLocaleDateString() : '-'}</span>
                    </div>
                    <div style="display:flex;justify-content:space-between;padding-bottom:8px;border-bottom:1px solid var(--border)">
                        <span style="color:var(--text-muted)">Last Seen</span>
                        <span style="font-weight:600;color:${d.isOnline ? '#22c55e' : '#ef4444'}">${timeAgo(d.lastSeen)}</span>
                    </div>
                    <div style="display:flex;justify-content:space-between">
                        <span style="color:var(--text-muted)">Processes</span>
                        <span style="font-weight:500">${d.processCount || '-'}</span>
                    </div>
                </div>
            </div>
        </div>

        <!-- Storage & Donuts Row -->
        <div style="display:grid;grid-template-columns:2fr 1fr 1fr;gap:12px;margin-bottom:16px">
            <!-- Storage Drives -->
            <div class="card" style="padding:20px">
                <h4 style="margin:0 0 16px;font-size:14px;display:flex;align-items:center;gap:8px">
                    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#f59e0b" stroke-width="2"><ellipse cx="12" cy="5" rx="9" ry="3"/><path d="M21 12c0 1.66-4 3-9 3s-9-1.34-9-3"/><path d="M3 5v14c0 1.66 4 3 9 3s9-1.34 9-3V5"/></svg>
                    Storage Drives
                </h4>
                ${(d.disks && d.disks.length > 0) ? d.disks.map(disk => {
                    const dColor = disk.usedPercent > 90 ? '#ef4444' : disk.usedPercent > 80 ? '#f59e0b' : '#22c55e';
                    return `<div style="margin-bottom:12px">
                        <div style="display:flex;justify-content:space-between;font-size:12px;margin-bottom:4px">
                            <span style="font-weight:600">${esc(disk.name)} Drive</span>
                            <span style="color:var(--text-muted)">${disk.freeGB ? disk.freeGB.toFixed(0) : '?'} GB free / ${disk.totalGB ? disk.totalGB.toFixed(0) : '?'} GB</span>
                        </div>
                        <div class="progress" style="height:10px"><div class="progress-bar" style="width:${disk.usedPercent}%;background:${dColor}"></div></div>
                        <div style="font-size:11px;color:var(--text-muted);margin-top:2px">${Math.round(disk.usedPercent)}% used</div>
                    </div>`;
                }).join('') : `
                    <div style="margin-bottom:12px">
                        <div style="display:flex;justify-content:space-between;font-size:12px;margin-bottom:4px">
                            <span style="font-weight:600">System Drive</span>
                            <span style="color:var(--text-muted)">${d.diskFreeGB ? d.diskFreeGB.toFixed(0) + ' GB free' : '-'}</span>
                        </div>
                        <div class="progress" style="height:10px"><div class="progress-bar" style="width:${d.diskPercent}%;background:${diskColor}"></div></div>
                        <div style="font-size:11px;color:var(--text-muted);margin-top:2px">${Math.round(d.diskPercent || 0)}% used</div>
                    </div>
                `}
            </div>

            <!-- Device Status Donut -->
            <div class="card" style="padding:20px;text-align:center">
                <h4 style="margin-bottom:12px;font-size:13px;color:var(--text-muted)">Protection Status</h4>
                <div id="dev-donut-protection"></div>
            </div>

            <!-- Resource Health Donut -->
            <div class="card" style="padding:20px;text-align:center">
                <h4 style="margin-bottom:12px;font-size:13px;color:var(--text-muted)">Resource Health</h4>
                <div id="dev-donut-resources"></div>
            </div>
        </div>

        <!-- Top Processes + Recent Alerts -->
        <div style="display:grid;grid-template-columns:1fr 1fr;gap:12px;margin-bottom:16px">
            <!-- Top Processes -->
            <div class="card" style="padding:20px">
                <h4 style="margin:0 0 12px;font-size:14px;display:flex;align-items:center;gap:8px">
                    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#06b6d4" stroke-width="2"><polyline points="22 12 18 12 15 21 9 3 6 12 2 12"/></svg>
                    Top Processes
                </h4>
                <table style="width:100%;font-size:12px">
                    <thead><tr><th style="text-align:left;padding:4px 8px">Process</th><th>CPU</th><th>Memory</th></tr></thead>
                    <tbody id="dev-processes">
                        <tr><td colspan="3" style="text-align:center;padding:16px;color:var(--text-muted)">Process data from agent</td></tr>
                    </tbody>
                </table>
            </div>

            <!-- Recent Device Alerts -->
            <div class="card" style="padding:20px">
                <h4 style="margin:0 0 12px;font-size:14px;display:flex;align-items:center;gap:8px">
                    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#ef4444" stroke-width="2"><path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9"/><path d="M13.73 21a2 2 0 0 1-3.46 0"/></svg>
                    Recent Alerts
                </h4>
                <div id="dev-alerts"></div>
            </div>
        </div>

        <!-- Modules Running -->
        <div class="card" style="padding:20px;margin-bottom:16px">
            <h4 style="margin:0 0 12px;font-size:14px;display:flex;align-items:center;gap:8px">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#22c55e" stroke-width="2"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/></svg>
                Active Protection Modules
            </h4>
            <div id="dev-modules" style="display:grid;grid-template-columns:repeat(auto-fill, minmax(200px, 1fr));gap:8px">
            </div>
        </div>

        <!-- Security Audit - 360 View -->
        <div class="card" style="padding:20px;margin-bottom:16px">
            <h4 style="margin:0 0 4px;font-size:16px;display:flex;align-items:center;gap:8px">
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="#3b82f6" stroke-width="2"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/></svg>
                360° Security Audit
            </h4>
            <div style="font-size:12px;color:var(--text-muted);margin-bottom:16px">Real-time security check results from endpoint agent</div>
            <div id="dev-security-audit"></div>
        </div>

        <!-- BitLocker Recovery Keys -->
        <div class="card" style="padding:20px;margin-bottom:16px">
            <h4 style="margin:0 0 12px;font-size:14px;display:flex;align-items:center;gap:8px">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#f59e0b" stroke-width="2"><rect x="3" y="11" width="18" height="11" rx="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/></svg>
                BitLocker Recovery Keys
            </h4>
            <div id="dev-bitlocker"></div>
        </div>
    `;

    // Load BitLocker keys
    loadBitLockerKeys(d.deviceId);

    // Populate donuts
    const protectedModules = d.runningModules || 0;
    createDonutChart('dev-donut-protection', [
        { label: 'Active', value: Math.max(protectedModules, 0), color: '#22c55e' },
        { label: 'Inactive', value: Math.max((d.totalModules || 6) - protectedModules, 0), color: '#6b7280' }
    ], protectedModules.toString(), 120);

    const cpuHealth = d.cpuPercent <= 70 ? 1 : 0;
    const ramHealth = d.ramPercent <= 80 ? 1 : 0;
    const diskHealth = d.diskPercent <= 85 ? 1 : 0;
    const tempHealth = (d.cpuTempC <= 75 || d.cpuTempC === 0) ? 1 : 0;
    const healthyCount = cpuHealth + ramHealth + diskHealth + tempHealth;
    createDonutChart('dev-donut-resources', [
        { label: 'Healthy', value: healthyCount, color: '#22c55e' },
        { label: 'Warning', value: 4 - healthyCount, color: '#f59e0b' }
    ], healthyCount + '/4', 120);

    // Populate security audit from securityChecksJson
    const auditEl = document.getElementById('dev-security-audit');
    let checks = [];
    try {
        if (d.securityChecksJson && d.securityChecksJson !== '[]')
            checks = JSON.parse(d.securityChecksJson).map(c => ({
                id: c.Id || c.id,
                name: c.Name || c.name,
                category: c.Category || c.category,
                passed: c.Passed != null ? c.Passed : c.passed,
                detail: c.Detail || c.detail,
                recommendation: c.Recommendation || c.recommendation,
                weight: c.Weight || c.weight,
                lastChecked: c.LastChecked || c.lastChecked
            }));
    } catch(e) {}

    if (checks.length > 0) {
        // Group by category
        const categories = {};
        checks.forEach(c => {
            if (!categories[c.category]) categories[c.category] = [];
            categories[c.category].push(c);
        });

        const catIcons = {
            'Protection': '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="#3b82f6" stroke-width="2"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/></svg>',
            'Updates': '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="#8b5cf6" stroke-width="2"><polyline points="1 4 1 10 7 10"/><path d="M3.51 15a9 9 0 1 0 2.13-9.36L1 10"/></svg>',
            'Data Protection': '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="#f59e0b" stroke-width="2"><rect x="3" y="11" width="18" height="11" rx="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/></svg>',
            'Network': '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="#06b6d4" stroke-width="2"><circle cx="12" cy="12" r="10"/><line x1="2" y1="12" x2="22" y2="12"/><path d="M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10z"/></svg>',
            'Access': '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="#ec4899" stroke-width="2"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>',
            'Identity & Access': '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="#ec4899" stroke-width="2"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>',
            'Ransomware Protection': '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="#ef4444" stroke-width="2"><rect x="3" y="11" width="18" height="11" rx="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/><circle cx="12" cy="16" r="1"/></svg>',
            'EDR & Advanced': '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="#10b981" stroke-width="2"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/><path d="M9 12l2 2 4-4"/></svg>',
            'Device Health': '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="#f59e0b" stroke-width="2"><path d="M22 12h-4l-3 9L9 3l-3 9H2"/></svg>'
        };

        auditEl.innerHTML = Object.entries(categories).map(([cat, catChecks]) => {
            const passed = catChecks.filter(c => c.passed).length;
            const total = catChecks.length;
            const allPassed = passed === total;
            const icon = catIcons[cat] || catIcons['Protection'];

            return `
                <div style="margin-bottom:16px">
                    <div style="display:flex;align-items:center;gap:8px;margin-bottom:8px;padding-bottom:6px;border-bottom:1px solid var(--border)">
                        ${icon}
                        <span style="font-weight:600;font-size:13px">${esc(cat)}</span>
                        <span style="margin-left:auto;font-size:12px;padding:2px 8px;border-radius:10px;background:${allPassed ? 'rgba(34,197,94,0.15)' : 'rgba(239,68,68,0.15)'};color:${allPassed ? '#22c55e' : '#ef4444'};font-weight:600">${passed}/${total} passed</span>
                    </div>
                    ${catChecks.map(c => {
                        const remediable = ['cfa','defender_rt','firewall','rdp','rdp_exposure','smbv1','uac','ps_logging','ps_exec_policy','guest','autologin','shadow_copies','asr_rules','lsass_protect','dns_security'].includes(c.id);
                        const manualOnly = ['tamper_protect','bitlocker','backup','edr','secure_boot'].includes(c.id);
                        return `
                        <div style="display:flex;align-items:flex-start;gap:10px;padding:8px 4px 8px 8px;border-radius:6px;margin-bottom:2px;background:${c.passed ? 'transparent' : 'rgba(239,68,68,0.05)'}">
                            <span style="font-size:14px;margin-top:1px;flex-shrink:0">${c.passed ? '<span style="color:#22c55e">&#10003;</span>' : '<span style="color:#ef4444">&#10007;</span>'}</span>
                            <div style="flex:1;min-width:0">
                                <div style="font-size:13px;font-weight:${c.passed ? '400' : '600'};color:${c.passed ? 'var(--text-primary)' : '#ef4444'}">${esc(c.name)}</div>
                                <div style="font-size:11px;color:var(--text-muted);margin-top:2px">${esc(c.detail)}</div>
                                ${c.lastChecked ? `<div style="font-size:10px;color:var(--text-muted);margin-top:1px;opacity:0.7">Validated: ${new Date(c.lastChecked).toLocaleString()}</div>` : ''}
                                ${!c.passed && c.recommendation ? `<div style="font-size:11px;color:#f59e0b;margin-top:2px;font-style:italic">&#9888; ${esc(c.recommendation)}</div>` : ''}
                            </div>
                            ${!c.passed && remediable ? `<button onclick="remediateCheck('${c.id}','${esc(c.name)}')" style="flex-shrink:0;background:#16a34a;color:#fff;border:none;padding:3px 10px;border-radius:4px;cursor:pointer;font-size:11px;font-weight:600" title="Auto-fix this issue">Fix</button>` : ''}
                            ${!c.passed && manualOnly ? `<span style="flex-shrink:0;font-size:10px;color:var(--text-muted);padding:3px 8px;border:1px solid var(--border);border-radius:4px" title="Requires manual action">Manual</span>` : ''}
                            <span style="font-size:10px;color:var(--text-muted);flex-shrink:0;padding:2px 6px;background:var(--bg-main);border-radius:4px">${c.weight}pts</span>
                        </div>`;
                    }).join('')}
                </div>
            `;
        }).join('');
    } else {
        auditEl.innerHTML = '<div style="text-align:center;padding:24px;color:var(--text-muted)">Security audit data will appear after the agent runs its first scan (runs every 30 minutes)</div>';
    }

    // Load device-specific alerts
    loadDeviceAlerts(d);

    // Populate modules
    const modulesEl = document.getElementById('dev-modules');
    const moduleNames = ['Health Monitor', 'Security Scanner', 'Ransomware Guard', 'Maintenance', 'Network Monitor', 'Process Monitor'];
    modulesEl.innerHTML = moduleNames.slice(0, Math.max(d.totalModules || 6, d.runningModules || 0)).map((name, i) => {
        const running = i < (d.runningModules || 0);
        return `<div style="display:flex;align-items:center;gap:8px;padding:10px;background:var(--bg-main);border-radius:6px">
            <div style="width:10px;height:10px;border-radius:50%;background:${running ? '#22c55e' : '#6b7280'}"></div>
            <span style="font-size:13px;font-weight:500">${name}</span>
            <span style="margin-left:auto;font-size:11px;color:${running ? '#22c55e' : 'var(--text-muted)'}">${running ? 'Running' : 'Stopped'}</span>
        </div>`;
    }).join('');
}

function generateDeviceReport() {
    if (!currentDevice) return;
    window.open('/report.html?deviceId=' + encodeURIComponent(currentDevice), '_blank');
}

function generateCompanyReport(mode) {
    if (!currentCustomer) return;
    if (mode === 'html') {
        window.open('/api/reports/company/' + encodeURIComponent(currentCustomer), '_blank');
    } else {
        window.open('/company-report.html?customer=' + encodeURIComponent(currentCustomer), '_blank');
    }
}

function reassignDevice(deviceId, currentCustomer) {
    const customers = [...new Set(allDevices.map(d => d.customerName).filter(Boolean))].sort();
    const hostname = (allDevices.find(d => d.deviceId === deviceId) || {}).hostname || deviceId;
    const modal = document.getElementById('device-modal');
    const content = document.getElementById('device-modal-content');
    content.innerHTML = `
        <h3>Move Device</h3>
        <p style="font-size:13px;color:var(--text-muted);margin-bottom:16px">Moving <strong>${esc(hostname)}</strong> from <strong>${esc(currentCustomer || 'Unassigned')}</strong></p>
        <div class="form-group">
            <label>Select Existing Customer</label>
            <select id="move-customer-select" class="input" onchange="document.getElementById('move-customer-new').value=this.value">
                <option value="">-- Select --</option>
                ${customers.map(c => `<option value="${esc(c)}" ${c===currentCustomer?'selected':''}>${esc(c)}</option>`).join('')}
            </select>
        </div>
        <div class="form-group">
            <label>Or Type New Customer Name</label>
            <input id="move-customer-new" class="input" value="${esc(currentCustomer || '')}" placeholder="Enter customer name">
        </div>
        <div style="display:flex;gap:8px;margin-top:16px">
            <button class="btn btn-primary" onclick="submitReassign('${deviceId}')">Move Device</button>
            <button class="btn btn-secondary" onclick="document.getElementById('device-modal').classList.remove('active')">Cancel</button>
        </div>`;
    modal.classList.add('active');
}

async function submitReassign(deviceId) {
    const newCustomer = document.getElementById('move-customer-new').value.trim();
    if (!newCustomer) { alert('Please select or enter a customer name'); return; }
    try {
        const res = await fetch(API + '/devices/' + encodeURIComponent(deviceId) + '/customer', {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ customerName: newCustomer })
        });
        if (res.ok) {
            document.getElementById('device-modal').classList.remove('active');
            const devLabel = document.getElementById('dev-customer-label');
            if (devLabel) devLabel.textContent = newCustomer;
            refreshCurrentPage();
        } else {
            alert('Failed to move device: ' + res.statusText);
        }
    } catch(e) {
        alert('Error: ' + e.message);
    }
}

async function remediateCheck(checkId, checkName) {
    if (!currentDevice) return;
    if (!confirm('Apply automatic fix for "' + checkName + '"?\n\nThis will be applied on the next agent heartbeat (within 30 seconds).')) return;

    try {
        const res = await fetch(API + '/devices/' + encodeURIComponent(currentDevice) + '/remediate', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ checkId: checkId })
        });
        if (res.ok) {
            const data = await res.json();
            alert('Fix queued successfully!\n\n' + (data.message || 'The fix will be applied on next heartbeat.') + '\n\nThe security score will update after the device re-scans.');
            // Refresh device detail after a short delay to show updated status
            setTimeout(() => refreshCurrentPage(), 2000);
        } else {
            alert('Failed to queue fix: ' + res.statusText);
        }
    } catch(e) {
        alert('Error: ' + e.message);
    }
}

async function loadDeviceAlerts(d) {
    try {
        const alerts = await fetch(API + '/alerts?limit=50&acknowledged=false').then(r => r.json());
        const devAlerts = alerts.filter(a => (a.hostname || a.deviceId) === (d.hostname || d.deviceId));
        const alertsEl = document.getElementById('dev-alerts');
        if (!alertsEl) return;

        if (devAlerts.length === 0) {
            alertsEl.innerHTML = '<div style="text-align:center;padding:20px;color:var(--text-muted)">No active alerts for this device</div>';
        } else {
            alertsEl.innerHTML = devAlerts.slice(0, 10).map(a => `
                <div class="alert-item" style="padding:8px 0;border-bottom:1px solid var(--border)">
                    <div class="alert-severity ${a.severity}"></div>
                    <div class="alert-content">
                        <div style="font-size:12px;font-weight:600">${esc(a.title)}</div>
                        <div style="font-size:11px;color:var(--text-muted)">${timeAgo(a.timestamp)} &middot; ${a.severity}</div>
                    </div>
                </div>
            `).join('');
        }
    } catch(err) { console.error('Failed to load device alerts', err); }
}

function closeModal() {
    document.getElementById('device-modal').classList.remove('active');
}

// --- Alerts ---
async function loadAlerts() {
    try {
        const severity = document.getElementById('alert-filter')?.value || '';
        const url = API + '/alerts?limit=100' + (severity ? '&severity=' + severity : '') + '&acknowledged=false';
        const alerts = await fetch(url).then(r => r.json());

        const feed = document.getElementById('alerts-feed');
        if (alerts.length === 0) {
            feed.innerHTML = '<div class="empty-state"><p>No alerts matching filter.</p></div>';
            return;
        }

        feed.innerHTML = alerts.map(a => `
            <div class="alert-item">
                <div class="alert-severity ${a.severity}"></div>
                <div class="alert-content">
                    <div class="alert-title">${esc(a.title)}</div>
                    <div class="alert-message">${esc(a.message)}</div>
                    <div class="alert-meta">${esc(a.hostname || a.deviceId)} &middot; ${a.moduleId} &middot; ${timeAgo(a.timestamp)} &middot; <span class="badge ${a.severity.toLowerCase()}">${a.severity}</span></div>
                </div>
                <button class="btn btn-sm btn-secondary" onclick="ackAlert(${a.id})">Acknowledge</button>
            </div>
        `).join('');
    } catch (err) {
        console.error('Failed to load alerts:', err);
    }
}

async function ackAlert(id) {
    await fetch(API + '/alerts/' + id + '/ack', { method: 'POST' });
    refreshCurrentPage();
}

// --- Incidents ---
async function loadIncidents() {
    try {
        const incidents = await fetch(API + '/incidents?limit=100').then(r => r.json());
        const tbody = document.getElementById('incidents-table');

        if (incidents.length === 0) {
            tbody.innerHTML = '<tr><td colspan="7" style="text-align:center;padding:32px;color:var(--text-muted)">No incidents recorded.</td></tr>';
            return;
        }

        tbody.innerHTML = incidents.map(i => `
            <tr>
                <td><span class="badge ${i.resolved ? 'online' : 'critical'}">${i.resolved ? 'Resolved' : 'Open'}</span></td>
                <td>${esc(i.hostname || i.deviceId)}</td>
                <td>${esc(i.type)}</td>
                <td style="max-width:300px;overflow:hidden;text-overflow:ellipsis">${esc(i.description)}</td>
                <td><span class="badge ${(i.severity||'info').toLowerCase()}">${i.severity}</span></td>
                <td style="font-size:12px">${timeAgo(i.occurredAt)}</td>
                <td>${!i.resolved ? `<button class="btn btn-sm btn-secondary" onclick="resolveIncident(${i.id})">Resolve</button>` : `<span style="font-size:12px;color:var(--text-muted)">${esc(i.resolvedBy)}</span>`}</td>
            </tr>
        `).join('');
    } catch (err) {
        console.error('Failed to load incidents:', err);
    }
}

async function resolveIncident(id) {
    await fetch(API + '/incidents/' + id + '/resolve', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ resolvedBy: 'dashboard', actionsTaken: 'Resolved via dashboard' })
    });
    loadIncidents();
}

// --- Policies ---
async function loadPolicies() {
    try {
        const policies = await fetch(API + '/policies').then(r => r.json());
        const grid = document.getElementById('policies-grid');

        grid.innerHTML = policies.map(p => {
            const config = JSON.parse(p.configJson || '{}');
            const configCount = Object.keys(config).length;
            return `
                <div class="stat-card">
                    <div class="label" style="text-transform:none;font-size:14px;color:var(--text-primary);font-weight:600">${esc(p.name)}</div>
                    <div class="sub" style="margin-top:4px">${esc(p.description)}</div>
                    <div style="margin-top:12px;font-size:12px;color:var(--text-muted)">${configCount} settings configured</div>
                    <div style="margin-top:8px;font-size:11px;color:var(--text-muted)">
                        ${Object.entries(config).slice(0, 4).map(([k,v]) => `${k}: ${v}`).join('<br>')}
                        ${configCount > 4 ? `<br>...and ${configCount - 4} more` : ''}
                    </div>
                </div>
            `;
        }).join('');
    } catch (err) {
        console.error('Failed to load policies:', err);
    }
}

// --- Config Push ---
async function loadConfigPage() {
    try {
        const [devices, policies] = await Promise.all([
            fetch(API + '/devices').then(r => r.json()),
            fetch(API + '/policies').then(r => r.json())
        ]);

        const deviceSelect = document.getElementById('config-device');
        deviceSelect.innerHTML = '<option value="">All Devices</option>' +
            devices.map(d => `<option value="${d.deviceId}">${esc(d.hostname || d.deviceId)}</option>`).join('');

        const profileSelect = document.getElementById('config-profile');
        profileSelect.innerHTML = '<option value="">No profile</option>' +
            policies.map(p => `<option value="${p.name}">${esc(p.name)}</option>`).join('');
    } catch (err) {
        console.error('Failed to load config page:', err);
    }
}

async function pushConfig() {
    const deviceId = document.getElementById('config-device').value;
    const profile = document.getElementById('config-profile').value;

    const config = {};

    // Scoring weights
    document.querySelectorAll('#scoring-weights input[data-key]').forEach(input => {
        config[input.dataset.key] = input.value;
    });

    // Thresholds
    config.scoringWarningThreshold = document.getElementById('cfg-warning').value;
    config.scoringContainmentThreshold = document.getElementById('cfg-containment').value;
    config.scoringLockdownThreshold = document.getElementById('cfg-lockdown').value;
    config.scoringDecayPerMinute = document.getElementById('cfg-decay').value;

    try {
        await fetch(API + '/config/push', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ deviceId: deviceId || null, policyProfile: profile || null, config })
        });
        alert('Configuration pushed! Endpoints will pick up changes on next heartbeat.');
    } catch (err) {
        alert('Failed to push config: ' + err.message);
    }
}

function resetConfigDefaults() {
    const defaults = { scoreHoneypotTriggered:50, scoreKnownRansomware:50, scoreShadowCopyDeletion:40,
        scoreRansomNoteCreation:35, scoreMultiFolderTouch:25, scoreMassExtensionChange:20,
        scoreSuspiciousPowerShell:20, scoreHighFileRenameRate:20, scoreHighEntropyWrite:15,
        scoreSuspiciousParentChild:15, scoreRiskyLaunchPath:10, scoreRansomwareExtension:10,
        scoreFileRename:5, scoreUnsignedProcess:5 };

    Object.entries(defaults).forEach(([key, val]) => {
        const input = document.querySelector(`input[data-key="${key}"]`);
        if (input) input.value = val;
    });
    document.getElementById('cfg-warning').value = 30;
    document.getElementById('cfg-containment').value = 60;
    document.getElementById('cfg-lockdown').value = 80;
    document.getElementById('cfg-decay').value = 5;
}

// --- Commands ---
async function sendCommand(deviceId, command) {
    try {
        await fetch(API + '/devices/' + deviceId + '/command', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ command })
        });
        alert('Command "' + command + '" sent. Device will execute on next heartbeat.');
        closeModal();
    } catch (err) {
        alert('Failed: ' + err.message);
    }
}

// --- Utilities ---
function esc(str) {
    if (!str) return '';
    const div = document.createElement('div');
    div.textContent = str;
    return div.innerHTML;
}

function timeAgo(dateStr) {
    if (!dateStr) return '-';
    const d = new Date(dateStr);
    const now = new Date();
    const diffMs = now - d;
    const diffMin = Math.floor(diffMs / 60000);
    const diffHr = Math.floor(diffMin / 60);
    const diffDay = Math.floor(diffHr / 24);

    if (diffMin < 1) return 'just now';
    if (diffMin < 60) return diffMin + 'm ago';
    if (diffHr < 24) return diffHr + 'h ago';
    if (diffDay < 7) return diffDay + 'd ago';
    return d.toLocaleDateString();
}

// --- Auto-refresh ---
function startAutoRefresh() {
    refreshInterval = setInterval(refreshCurrentPage, 15000); // 15 seconds
}

// --- Init ---
document.addEventListener('DOMContentLoaded', () => {
    loadOverview();
    startAutoRefresh();
});

// Close modal on overlay click
document.getElementById('device-modal').addEventListener('click', (e) => {
    if (e.target === document.getElementById('device-modal')) closeModal();
});

// --- BitLocker Recovery Keys ---
async function loadBitLockerKeys(deviceId) {
    const el = document.getElementById('dev-bitlocker');
    if (!el) return;
    try {
        const res = await apiFetch(API + '/devices/' + encodeURIComponent(deviceId) + '/bitlocker');
        if (!res) return;
        const keys = await res.json();
        if (!keys || keys.length === 0) {
            el.innerHTML = '<div style="text-align:center;padding:16px;color:var(--text-muted);font-size:13px">No BitLocker recovery keys captured yet. Keys will appear after the agent scans encrypted drives.</div>';
            return;
        }
        el.innerHTML = `<table style="width:100%">
            <thead><tr><th>Drive</th><th>Key Protector ID</th><th>Recovery Key</th><th></th></tr></thead>
            <tbody>${keys.map(k => `<tr>
                <td style="font-weight:600;font-size:14px">${esc(k.driveLetter || k.DriveLetter)}</td>
                <td style="font-size:11px;color:var(--text-muted);font-family:monospace">${esc(k.keyProtectorId || k.KeyProtectorId)}</td>
                <td>
                    <span class="bl-key" id="blk-${esc(k.keyProtectorId || k.KeyProtectorId)}" style="font-family:monospace;font-size:12px;letter-spacing:0.5px">
                        ${'*'.repeat(20)}
                    </span>
                    <span class="bl-key-val" style="display:none">${esc(k.recoveryKey || k.RecoveryKey)}</span>
                </td>
                <td style="white-space:nowrap">
                    <button onclick="toggleBLKey(this)" class="btn btn-sm btn-secondary" style="font-size:11px;padding:2px 8px">Reveal</button>
                    <button onclick="copyBLKey('${esc(k.recoveryKey || k.RecoveryKey)}')" class="btn btn-sm btn-primary" style="font-size:11px;padding:2px 8px;background:#3b82f6">Copy</button>
                </td>
            </tr>`).join('')}</tbody>
        </table>`;
    } catch (err) {
        el.innerHTML = '<div style="color:var(--text-muted);font-size:12px">Failed to load BitLocker keys</div>';
    }
}

function toggleBLKey(btn) {
    const row = btn.closest('tr');
    const masked = row.querySelector('.bl-key');
    const real = row.querySelector('.bl-key-val');
    if (masked.style.display !== 'none') {
        masked.style.display = 'none';
        real.style.display = 'inline';
        real.style.fontFamily = 'monospace';
        real.style.fontSize = '12px';
        real.style.letterSpacing = '0.5px';
        btn.textContent = 'Hide';
    } else {
        masked.style.display = 'inline';
        real.style.display = 'none';
        btn.textContent = 'Reveal';
    }
}

function copyBLKey(key) {
    navigator.clipboard.writeText(key).then(() => {
        alert('Recovery key copied to clipboard');
    });
}

// --- Email Reports ---
async function loadEmailReports() {
    try {
        const [schedules, smtpRes, customers] = await Promise.all([
            apiFetch(API + '/email-schedules').then(r => r?.json() || []),
            apiFetch(API + '/email-schedules/smtp').then(r => r?.json() || {}),
            apiFetch('/api/reports/customers').then(r => r?.json() || [])
        ]);

        const content = document.getElementById('email-reports-content');
        const daysOfWeek = ['Sunday','Monday','Tuesday','Wednesday','Thursday','Friday','Saturday'];

        content.innerHTML = `
            <!-- SMTP Settings -->
            <div class="card" style="padding:20px;margin-bottom:16px">
                <h4 style="margin:0 0 16px;font-size:14px">SMTP Configuration</h4>
                <div class="grid-3" style="gap:12px">
                    <div><label style="font-size:12px;color:var(--text-muted)">SMTP Host</label><input id="smtp-host" value="${esc(smtpRes.host||'')}" class="input" placeholder="smtp.gmail.com"></div>
                    <div><label style="font-size:12px;color:var(--text-muted)">Port</label><input id="smtp-port" value="${smtpRes.port||587}" type="number" class="input"></div>
                    <div><label style="font-size:12px;color:var(--text-muted)">SSL/TLS</label><select id="smtp-ssl" class="input"><option value="true" ${smtpRes.useSsl!==false?'selected':''}>Enabled</option><option value="false" ${smtpRes.useSsl===false?'selected':''}>Disabled</option></select></div>
                    <div><label style="font-size:12px;color:var(--text-muted)">Username</label><input id="smtp-user" value="${esc(smtpRes.username||'')}" class="input"></div>
                    <div><label style="font-size:12px;color:var(--text-muted)">Password</label><input id="smtp-pass" value="${esc(smtpRes.password||'')}" type="password" class="input"></div>
                    <div><label style="font-size:12px;color:var(--text-muted)">From Address</label><input id="smtp-from" value="${esc(smtpRes.fromAddress||'')}" class="input" placeholder="reports@company.com"></div>
                    <div><label style="font-size:12px;color:var(--text-muted)">From Name</label><input id="smtp-name" value="${esc(smtpRes.fromName||'PC Plus Computing')}" class="input"></div>
                </div>
                <div style="margin-top:12px;display:flex;gap:8px">
                    <button class="btn btn-sm btn-primary" onclick="saveSmtp()">Save SMTP</button>
                    <button class="btn btn-sm btn-secondary" onclick="testSmtp()">Test Connection</button>
                </div>
            </div>

            <!-- Add Schedule -->
            <div class="card" style="padding:20px;margin-bottom:16px">
                <h4 style="margin:0 0 16px;font-size:14px">Add Email Schedule</h4>
                <div class="grid-3" style="gap:12px">
                    <div><label style="font-size:12px;color:var(--text-muted)">Customer</label>
                        <select id="sched-customer" class="input">
                            ${customers.map(c => `<option value="${esc(c.name)}">${esc(c.name)} (${c.deviceCount} devices)</option>`).join('')}
                        </select>
                    </div>
                    <div><label style="font-size:12px;color:var(--text-muted)">Recipient Emails (comma-separated)</label><input id="sched-emails" class="input" placeholder="client@company.com"></div>
                    <div><label style="font-size:12px;color:var(--text-muted)">Frequency</label>
                        <select id="sched-freq" class="input"><option value="weekly">Weekly</option><option value="biweekly">Biweekly</option><option value="monthly">Monthly</option></select>
                    </div>
                    <div><label style="font-size:12px;color:var(--text-muted)">Day of Week</label>
                        <select id="sched-day" class="input">${daysOfWeek.map((d,i) => `<option value="${i}" ${i===1?'selected':''}>${d}</option>`).join('')}</select>
                    </div>
                    <div><label style="font-size:12px;color:var(--text-muted)">Hour (UTC)</label><input id="sched-hour" type="number" min="0" max="23" value="8" class="input"></div>
                </div>
                <div style="margin-top:12px">
                    <button class="btn btn-sm btn-primary" onclick="createSchedule()">Add Schedule</button>
                </div>
            </div>

            <!-- Schedules List -->
            <div class="card" style="padding:20px">
                <h4 style="margin:0 0 16px;font-size:14px">Active Schedules</h4>
                <div class="table-scroll">
                <table style="width:100%;min-width:700px">
                    <thead><tr><th>Customer</th><th>Recipients</th><th>Frequency</th><th>Day/Hour</th><th>Last Sent</th><th>Next Send</th><th>Status</th><th>Actions</th></tr></thead>
                    <tbody id="schedules-table">
                        ${schedules.length === 0 ? '<tr><td colspan="8" style="text-align:center;padding:24px;color:var(--text-muted)">No email schedules configured yet</td></tr>' :
                        schedules.map(s => `<tr>
                            <td style="font-weight:600">${esc(s.customerName)}</td>
                            <td style="font-size:11px;max-width:200px;overflow:hidden;text-overflow:ellipsis">${esc(s.recipientEmails)}</td>
                            <td>${s.frequency}</td>
                            <td>${daysOfWeek[s.dayOfWeek] || '?'} ${s.hour}:00 UTC</td>
                            <td style="font-size:12px">${s.lastSentAt ? timeAgo(s.lastSentAt) : 'Never'}</td>
                            <td style="font-size:12px">${s.nextSendAt ? new Date(s.nextSendAt).toLocaleDateString() : '-'}</td>
                            <td><span class="badge ${s.enabled ? 'online' : 'offline'}">${s.enabled ? 'Active' : 'Paused'}</span></td>
                            <td style="white-space:nowrap">
                                <button class="btn btn-sm btn-primary" onclick="sendNow(${s.id})" style="font-size:11px;padding:2px 8px">Send Now</button>
                                <button class="btn btn-sm btn-secondary" onclick="toggleSchedule(${s.id})" style="font-size:11px;padding:2px 8px">${s.enabled ? 'Pause' : 'Enable'}</button>
                                <button class="btn btn-sm btn-danger" onclick="deleteSchedule(${s.id})" style="font-size:11px;padding:2px 8px">Delete</button>
                            </td>
                        </tr>`).join('')}
                    </tbody>
                </table>
                </div>
            </div>
        `;
    } catch (err) {
        console.error('Failed to load email reports:', err);
    }
}

async function saveSmtp() {
    const data = {
        host: document.getElementById('smtp-host').value,
        port: parseInt(document.getElementById('smtp-port').value) || 587,
        username: document.getElementById('smtp-user').value,
        password: document.getElementById('smtp-pass').value,
        fromAddress: document.getElementById('smtp-from').value,
        fromName: document.getElementById('smtp-name').value,
        useSsl: document.getElementById('smtp-ssl').value === 'true'
    };
    const res = await apiFetch(API + '/email-schedules/smtp', {
        method: 'POST', headers: {'Content-Type':'application/json'}, body: JSON.stringify(data)
    });
    if (res?.ok) alert('SMTP settings saved!');
}

async function testSmtp() {
    const data = {
        host: document.getElementById('smtp-host').value,
        port: parseInt(document.getElementById('smtp-port').value) || 587,
        username: document.getElementById('smtp-user').value,
        password: document.getElementById('smtp-pass').value,
        fromAddress: document.getElementById('smtp-from').value,
        fromName: document.getElementById('smtp-name').value,
        useSsl: document.getElementById('smtp-ssl').value === 'true'
    };
    const res = await apiFetch(API + '/email-schedules/smtp/test', {
        method: 'POST', headers: {'Content-Type':'application/json'}, body: JSON.stringify(data)
    });
    if (!res) return;
    const result = await res.json();
    alert(result.success ? 'SMTP test successful! Check your inbox.' : 'SMTP test failed: ' + result.message);
}

async function createSchedule() {
    const data = {
        customerName: document.getElementById('sched-customer').value,
        recipientEmails: document.getElementById('sched-emails').value,
        frequency: document.getElementById('sched-freq').value,
        dayOfWeek: parseInt(document.getElementById('sched-day').value),
        hour: parseInt(document.getElementById('sched-hour').value)
    };
    if (!data.customerName || !data.recipientEmails) { alert('Please fill in customer and email fields'); return; }
    await apiFetch(API + '/email-schedules', {
        method: 'POST', headers: {'Content-Type':'application/json'}, body: JSON.stringify(data)
    });
    loadEmailReports();
}

async function toggleSchedule(id) {
    await apiFetch(API + '/email-schedules/' + id + '/toggle', { method: 'POST' });
    loadEmailReports();
}

async function deleteSchedule(id) {
    if (!confirm('Delete this schedule?')) return;
    await apiFetch(API + '/email-schedules/' + id, { method: 'DELETE' });
    loadEmailReports();
}

async function sendNow(id) {
    if (!confirm('Send report now?')) return;
    const res = await apiFetch(API + '/email-schedules/' + id + '/send-now', { method: 'POST' });
    if (!res) return;
    const result = await res.json();
    if (result.error) alert('Error: ' + result.error);
    else alert('Report sent to ' + result.recipients + ' recipient(s)!');
    loadEmailReports();
}

// === USER MANAGEMENT ===
async function loadUsers() {
    try {
        const users = await apiFetch('/api/auth/users').then(r => r?.json() || []);
        const content = document.getElementById('users-content');
        content.innerHTML = `
            <div class="card" style="padding:20px;margin-bottom:16px">
                <h4 style="margin:0 0 16px;font-size:14px">Change My Password</h4>
                <div class="grid-3" style="gap:12px">
                    <div><label style="font-size:12px;color:var(--text-muted)">Current Password</label><input id="pw-current" type="password" class="input"></div>
                    <div><label style="font-size:12px;color:var(--text-muted)">New Password</label><input id="pw-new" type="password" class="input"></div>
                    <div><label style="font-size:12px;color:var(--text-muted)">Confirm New Password</label><input id="pw-confirm" type="password" class="input"></div>
                </div>
                <div style="margin-top:12px"><button class="btn btn-sm btn-primary" onclick="changePassword()">Change Password</button></div>
            </div>
            <div class="card">
                <div class="card-header"><h3>Users</h3></div>
                <div class="table-scroll">
                <table style="width:100%;min-width:600px">
                    <thead><tr><th>Username</th><th>Display Name</th><th>Role</th><th>Last Login</th><th>Created</th><th>Actions</th></tr></thead>
                    <tbody>
                        ${users.map(u => `<tr>
                            <td style="font-weight:600">${esc(u.username)}</td>
                            <td>${esc(u.displayName)}</td>
                            <td><span class="badge ${u.role==='admin'?'critical':'info'}">${u.role}</span></td>
                            <td style="font-size:12px">${u.lastLogin ? timeAgo(u.lastLogin) : 'Never'}</td>
                            <td style="font-size:12px">${new Date(u.createdAt).toLocaleDateString()}</td>
                            <td style="white-space:nowrap">
                                <button class="btn btn-sm btn-secondary" onclick="editUser(${u.id},'${esc(u.role)}','${esc(u.displayName)}')" style="font-size:11px;padding:2px 8px">Edit</button>
                                <button class="btn btn-sm btn-secondary" onclick="resetUserPassword(${u.id},'${esc(u.username)}')" style="font-size:11px;padding:2px 8px">Reset PW</button>
                                <button class="btn btn-sm btn-danger" onclick="deleteUser(${u.id},'${esc(u.username)}')" style="font-size:11px;padding:2px 8px">Delete</button>
                            </td>
                        </tr>`).join('')}
                    </tbody>
                </table>
                </div>
            </div>`;
    } catch (err) { console.error('Failed to load users:', err); }
}

function showAddUser() {
    const modal = document.getElementById('device-modal');
    const content = document.getElementById('device-modal-content');
    content.innerHTML = `
        <h3>Add New User</h3>
        <div class="form-group"><label>Username</label><input id="new-username" class="input"></div>
        <div class="form-group"><label>Display Name</label><input id="new-displayname" class="input"></div>
        <div class="form-group"><label>Password</label><input id="new-password" type="password" class="input"></div>
        <div class="form-group"><label>Role</label><select id="new-role" class="input" onchange="document.getElementById('new-customer-row').style.display=this.value==='customer'?'block':'none'"><option value="admin">Admin</option><option value="operator">Operator</option><option value="viewer">Viewer</option><option value="customer">Customer Portal</option></select></div>
        <div class="form-group" id="new-customer-row" style="display:none"><label>Customer Name (must match exactly)</label><input id="new-customer" class="input" placeholder="e.g. 108 Ave Hospital"></div>
        <div style="display:flex;gap:8px;margin-top:16px">
            <button class="btn btn-primary" onclick="createUser()">Create User</button>
            <button class="btn btn-secondary" onclick="document.getElementById('device-modal').classList.remove('active')">Cancel</button>
        </div>`;
    modal.classList.add('active');
}

async function createUser() {
    const data = {
        username: document.getElementById('new-username').value,
        displayName: document.getElementById('new-displayname').value,
        password: document.getElementById('new-password').value,
        role: document.getElementById('new-role').value,
        customerName: document.getElementById('new-customer')?.value || ''
    };
    if (!data.username || !data.password) { alert('Username and password are required'); return; }
    const res = await apiFetch('/api/auth/users', {
        method: 'POST', headers: {'Content-Type':'application/json'}, body: JSON.stringify(data)
    });
    if (res?.ok) {
        document.getElementById('device-modal').classList.remove('active');
        alert('User created!');
        loadUsers();
    } else {
        const err = await res?.json();
        alert('Error: ' + (err?.error || 'Failed to create user'));
    }
}

function editUser(id, role, displayName) {
    const modal = document.getElementById('device-modal');
    const content = document.getElementById('device-modal-content');
    content.innerHTML = `
        <h3>Edit User</h3>
        <div class="form-group"><label>Display Name</label><input id="edit-displayname" value="${esc(displayName)}" class="input"></div>
        <div class="form-group"><label>Role</label><select id="edit-role" class="input"><option value="admin" ${role==='admin'?'selected':''}>Admin</option><option value="operator" ${role==='operator'?'selected':''}>Operator</option><option value="viewer" ${role==='viewer'?'selected':''}>Viewer</option></select></div>
        <div style="display:flex;gap:8px;margin-top:16px">
            <button class="btn btn-primary" onclick="updateUser(${id})">Save</button>
            <button class="btn btn-secondary" onclick="document.getElementById('device-modal').classList.remove('active')">Cancel</button>
        </div>`;
    modal.classList.add('active');
}

async function updateUser(id) {
    const data = {
        displayName: document.getElementById('edit-displayname').value,
        role: document.getElementById('edit-role').value
    };
    const res = await apiFetch('/api/auth/users/' + id, {
        method: 'PUT', headers: {'Content-Type':'application/json'}, body: JSON.stringify(data)
    });
    if (res?.ok) {
        document.getElementById('device-modal').classList.remove('active');
        loadUsers();
    } else {
        const err = await res?.json();
        alert('Error: ' + (err?.error || 'Failed to update user'));
    }
}

async function deleteUser(id, username) {
    if (!confirm('Delete user "' + username + '"?')) return;
    const res = await apiFetch('/api/auth/users/' + id, { method: 'DELETE' });
    if (res?.ok) loadUsers();
    else { const err = await res?.json(); alert('Error: ' + (err?.error || 'Failed')); }
}

async function changePassword() {
    const current = document.getElementById('pw-current').value;
    const newPw = document.getElementById('pw-new').value;
    const confirm = document.getElementById('pw-confirm').value;
    if (!current || !newPw) { alert('Please fill in all fields'); return; }
    if (newPw !== confirm) { alert('New passwords do not match'); return; }
    const pwErr = validatePassword(newPw);
    if (pwErr) { alert(pwErr); return; }
    const res = await apiFetch('/api/auth/change-password', {
        method: 'POST', headers: {'Content-Type':'application/json'},
        body: JSON.stringify({ currentPassword: current, newPassword: newPw })
    });
    if (res?.ok) {
        alert('Password changed successfully!');
        document.getElementById('pw-current').value = '';
        document.getElementById('pw-new').value = '';
        document.getElementById('pw-confirm').value = '';
    } else {
        const err = await res?.json();
        alert('Error: ' + (err?.error || 'Failed to change password'));
    }
}

function validatePassword(pw) {
    if (pw.length < 8) return 'Password must be at least 8 characters';
    if (!/[A-Z]/.test(pw)) return 'Password must contain at least one uppercase letter';
    if (!/[a-z]/.test(pw)) return 'Password must contain at least one lowercase letter';
    if (!/[0-9]/.test(pw)) return 'Password must contain at least one number';
    if (!/[^A-Za-z0-9]/.test(pw)) return 'Password must contain at least one special character';
    return null;
}

function resetUserPassword(id, username) {
    const modal = document.getElementById('device-modal');
    const content = document.getElementById('device-modal-content');
    content.innerHTML = `
        <h3>Reset Password for ${esc(username)}</h3>
        <p style="font-size:12px;color:var(--text-muted);margin-bottom:12px">Password must be at least 8 characters with uppercase, lowercase, number, and special character.</p>
        <div class="form-group"><label>New Password</label><input id="reset-pw" type="password" class="input"></div>
        <div class="form-group"><label>Confirm Password</label><input id="reset-pw-confirm" type="password" class="input"></div>
        <div style="display:flex;gap:8px;margin-top:16px">
            <button class="btn btn-primary" onclick="submitResetPassword(${id})">Reset Password</button>
            <button class="btn btn-secondary" onclick="document.getElementById('device-modal').classList.remove('active')">Cancel</button>
        </div>`;
    modal.classList.add('active');
}

async function submitResetPassword(id) {
    const newPw = document.getElementById('reset-pw').value;
    const confirmPw = document.getElementById('reset-pw-confirm').value;
    if (!newPw) { alert('Please enter a new password'); return; }
    if (newPw !== confirmPw) { alert('Passwords do not match'); return; }
    const pwErr = validatePassword(newPw);
    if (pwErr) { alert(pwErr); return; }
    const res = await apiFetch('/api/auth/users/' + id + '/reset-password', {
        method: 'POST', headers: {'Content-Type':'application/json'},
        body: JSON.stringify({ newPassword: newPw })
    });
    if (res?.ok) {
        document.getElementById('device-modal').classList.remove('active');
        alert('Password reset successfully!');
    } else {
        const err = await res?.json();
        alert('Error: ' + (err?.error || 'Failed to reset password'));
    }
}

// === NOTIFICATIONS / WEBHOOKS ===
async function loadNotifications() {
    try {
        const configs = await apiFetch(API + '/notifications').then(r => r?.json() || []);
        const content = document.getElementById('notifications-content');
        content.innerHTML = `
            <div class="card" style="padding:20px;margin-bottom:16px">
                <p style="color:var(--text-secondary);font-size:13px;margin-bottom:0">Configure webhook endpoints to receive real-time notifications when critical alerts fire. Supports generic webhooks, Slack, and Microsoft Teams.</p>
            </div>
            <div class="card">
                <div class="card-header"><h3>Configured Webhooks</h3></div>
                <div class="table-scroll">
                <table style="width:100%;min-width:700px">
                    <thead><tr><th>Name</th><th>Type</th><th>URL</th><th>Min Severity</th><th>Status</th><th>Actions</th></tr></thead>
                    <tbody>
                        ${configs.length === 0 ? '<tr><td colspan="6" style="text-align:center;padding:24px;color:var(--text-muted)">No webhooks configured yet. Click "+ Add Webhook" to get started.</td></tr>' :
                        configs.map(c => `<tr>
                            <td style="font-weight:600">${esc(c.name)}</td>
                            <td><span class="badge ${c.type==='slack'?'info':c.type==='teams'?'info':'warning'}">${c.type}</span></td>
                            <td style="font-size:11px;max-width:250px;overflow:hidden;text-overflow:ellipsis;font-family:monospace">${esc(c.webhookUrl)}</td>
                            <td><span class="badge ${c.minSeverity==='Critical'||c.minSeverity==='Emergency'?'critical':'warning'}">${c.minSeverity}</span></td>
                            <td><span class="badge ${c.enabled?'online':'offline'}">${c.enabled?'Active':'Disabled'}</span></td>
                            <td style="white-space:nowrap">
                                <button class="btn btn-sm btn-primary" onclick="testNotification(${c.id})" style="font-size:11px;padding:2px 8px">Test</button>
                                <button class="btn btn-sm btn-secondary" onclick="toggleNotification(${c.id})" style="font-size:11px;padding:2px 8px">${c.enabled?'Disable':'Enable'}</button>
                                <button class="btn btn-sm btn-danger" onclick="deleteNotification(${c.id})" style="font-size:11px;padding:2px 8px">Delete</button>
                            </td>
                        </tr>`).join('')}
                    </tbody>
                </table>
                </div>
            </div>`;
    } catch (err) { console.error('Failed to load notifications:', err); }
}

function showAddNotification() {
    const modal = document.getElementById('device-modal');
    const content = document.getElementById('device-modal-content');
    content.innerHTML = `
        <h3>Add Webhook Notification</h3>
        <div class="form-group"><label>Name</label><input id="notif-name" class="input" placeholder="e.g. Slack - Security Alerts"></div>
        <div class="form-group"><label>Type</label><select id="notif-type" class="input"><option value="webhook">Generic Webhook</option><option value="slack">Slack</option><option value="teams">Microsoft Teams</option></select></div>
        <div class="form-group"><label>Webhook URL</label><input id="notif-url" class="input" placeholder="https://hooks.slack.com/services/..."></div>
        <div class="form-group"><label>Minimum Severity</label><select id="notif-severity" class="input"><option value="Info">Info (all alerts)</option><option value="Warning">Warning+</option><option value="Critical" selected>Critical+</option><option value="Emergency">Emergency only</option></select></div>
        <div style="display:flex;gap:8px;margin-top:16px">
            <button class="btn btn-primary" onclick="createNotification()">Add Webhook</button>
            <button class="btn btn-secondary" onclick="document.getElementById('device-modal').classList.remove('active')">Cancel</button>
        </div>`;
    modal.classList.add('active');
}

async function createNotification() {
    const data = {
        name: document.getElementById('notif-name').value,
        type: document.getElementById('notif-type').value,
        webhookUrl: document.getElementById('notif-url').value,
        minSeverity: document.getElementById('notif-severity').value,
        enabled: true
    };
    if (!data.name || !data.webhookUrl) { alert('Name and URL are required'); return; }
    const res = await apiFetch(API + '/notifications', {
        method: 'POST', headers: {'Content-Type':'application/json'}, body: JSON.stringify(data)
    });
    if (res?.ok) {
        document.getElementById('device-modal').classList.remove('active');
        alert('Webhook added!');
        loadNotifications();
    }
}

async function testNotification(id) {
    const res = await apiFetch(API + '/notifications/' + id + '/test', { method: 'POST' });
    if (!res) return;
    const result = await res.json();
    alert(result.success ? 'Test notification sent!' : 'Test failed: ' + (result.error || 'Unknown error'));
}

async function toggleNotification(id) {
    // Toggle by updating with opposite enabled state
    const configs = await apiFetch(API + '/notifications').then(r => r?.json() || []);
    const config = configs.find(c => c.id === id);
    if (!config) return;
    await apiFetch(API + '/notifications/' + id, {
        method: 'PUT', headers: {'Content-Type':'application/json'},
        body: JSON.stringify({ ...config, enabled: !config.enabled })
    });
    loadNotifications();
}

async function deleteNotification(id) {
    if (!confirm('Delete this webhook?')) return;
    await apiFetch(API + '/notifications/' + id, { method: 'DELETE' });
    loadNotifications();
}

// === HISTORICAL TRENDS ===
function drawTrendChart(canvasId, data, color, label, maxVal) {
    const canvas = document.getElementById(canvasId);
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    const dpr = window.devicePixelRatio || 1;
    const rect = canvas.parentElement.getBoundingClientRect();
    canvas.width = (rect.width - 40) * dpr;
    canvas.height = 200 * dpr;
    canvas.style.width = (rect.width - 40) + 'px';
    canvas.style.height = '200px';
    ctx.scale(dpr, dpr);

    const w = rect.width - 40, h = 200;
    const padding = { top: 10, right: 10, bottom: 30, left: 45 };
    const plotW = w - padding.left - padding.right;
    const plotH = h - padding.top - padding.bottom;

    ctx.clearRect(0, 0, w, h);

    if (!data || data.length === 0) {
        ctx.fillStyle = '#6b7280'; ctx.font = '13px Segoe UI'; ctx.textAlign = 'center';
        ctx.fillText('No trend data yet - collecting every 30 minutes', w / 2, h / 2);
        return;
    }

    const max = maxVal || Math.max(...data.map(d => d.value), 1);

    // Grid
    ctx.strokeStyle = 'rgba(255,255,255,0.06)'; ctx.lineWidth = 1;
    for (let i = 0; i <= 4; i++) {
        const y = padding.top + (plotH / 4) * i;
        ctx.beginPath(); ctx.moveTo(padding.left, y); ctx.lineTo(w - padding.right, y); ctx.stroke();
        ctx.fillStyle = '#6b7280'; ctx.font = '10px Segoe UI'; ctx.textAlign = 'right';
        ctx.fillText(Math.round(max - (max / 4) * i), padding.left - 6, y + 4);
    }

    // Line
    ctx.beginPath(); ctx.strokeStyle = color; ctx.lineWidth = 2; ctx.lineJoin = 'round';
    data.forEach((d, i) => {
        const x = padding.left + (i / Math.max(data.length - 1, 1)) * plotW;
        const y = padding.top + plotH - (d.value / max) * plotH;
        if (i === 0) ctx.moveTo(x, y); else ctx.lineTo(x, y);
    });
    ctx.stroke();

    // Fill
    const lastIdx = data.length - 1;
    ctx.lineTo(padding.left + (lastIdx / Math.max(data.length - 1, 1)) * plotW, padding.top + plotH);
    ctx.lineTo(padding.left, padding.top + plotH);
    ctx.closePath();
    const gradient = ctx.createLinearGradient(0, padding.top, 0, padding.top + plotH);
    gradient.addColorStop(0, color.replace(')', ',0.15)').replace('rgb', 'rgba'));
    gradient.addColorStop(1, color.replace(')', ',0)').replace('rgb', 'rgba'));
    ctx.fillStyle = gradient;
    ctx.fill();

    // X labels
    ctx.fillStyle = '#6b7280'; ctx.font = '10px Segoe UI'; ctx.textAlign = 'center';
    const step = Math.max(1, Math.floor(data.length / 6));
    data.forEach((d, i) => {
        if (i % step === 0 || i === lastIdx) {
            const x = padding.left + (i / Math.max(data.length - 1, 1)) * plotW;
            ctx.fillText(d.label, x, h - 8);
        }
    });
}

async function loadTrends() {
    try {
        const days = document.getElementById('trend-range')?.value || 30;
        const customer = document.getElementById('trend-customer')?.value || '';

        // Populate customer dropdown if empty
        const customerSelect = document.getElementById('trend-customer');
        if (customerSelect && customerSelect.options.length <= 1) {
            const devices = await apiFetch(API + '/devices').then(r => r?.json() || []);
            const customers = [...new Set(devices.map(d => d.customerName).filter(Boolean))].sort();
            customers.forEach(c => {
                const opt = document.createElement('option');
                opt.value = c; opt.textContent = c;
                customerSelect.appendChild(opt);
            });
        }

        const url = customer
            ? API + '/history/customer/' + encodeURIComponent(customer) + '?days=' + days
            : API + '/history/overview?days=' + days;
        const history = await apiFetch(url).then(r => r?.json() || []);

        const secData = history.map(h => ({ label: new Date(h.date).toLocaleDateString('en-US', {month:'short',day:'numeric'}), value: h.avgSecurityScore || 0 }));
        const cpuData = history.map(h => ({ label: new Date(h.date).toLocaleDateString('en-US', {month:'short',day:'numeric'}), value: h.avgCpuPercent || 0 }));
        const ramData = history.map(h => ({ label: new Date(h.date).toLocaleDateString('en-US', {month:'short',day:'numeric'}), value: h.avgRamPercent || 0 }));
        const diskData = history.map(h => ({ label: new Date(h.date).toLocaleDateString('en-US', {month:'short',day:'numeric'}), value: h.avgDiskPercent || 0 }));

        drawTrendChart('trend-security', secData, 'rgb(59, 130, 246)', 'Security Score', 100);
        drawTrendChart('trend-cpu', cpuData, 'rgb(249, 115, 22)', 'CPU %', 100);
        drawTrendChart('trend-ram', ramData, 'rgb(168, 85, 247)', 'RAM %', 100);
        drawTrendChart('trend-disk', diskData, 'rgb(34, 197, 94)', 'Disk %', 100);
    } catch (err) { console.error('Failed to load trends:', err); }
}

// === BRANDING ===
async function loadBranding() {
    const content = document.getElementById('branding-content');
    let currentLogo = '';
    let currentName = '';
    let currentTagline = '';
    try {
        const res = await apiFetch(API + '/branding');
        if (res?.ok) {
            const data = await res.json();
            currentLogo = data.logoUrl || '';
            currentName = data.companyName || 'PC Plus Computing';
            currentTagline = data.tagline || 'Endpoint Protection';
        }
    } catch {}

    content.innerHTML = `
        <div class="card" style="padding:24px;margin-bottom:16px">
            <h4 style="margin:0 0 20px;font-size:14px">Company Branding</h4>
            <div class="grid-2x2" style="gap:16px">
                <div>
                    <div class="form-group"><label>Company Name</label><input id="brand-name" value="${esc(currentName)}" class="input"></div>
                    <div class="form-group"><label>Tagline</label><input id="brand-tagline" value="${esc(currentTagline)}" class="input" placeholder="Endpoint Protection"></div>
                    <div class="form-group">
                        <label>Logo (URL or upload)</label>
                        <input id="brand-logo-url" value="${esc(currentLogo)}" class="input" placeholder="https://example.com/logo.png">
                        <div style="margin-top:8px">
                            <input type="file" id="brand-logo-file" accept="image/*" onchange="uploadLogo()" style="font-size:12px">
                        </div>
                    </div>
                    <button class="btn btn-primary" onclick="saveBranding()">Save Branding</button>
                </div>
                <div style="text-align:center;padding:24px">
                    <h4 style="color:var(--text-muted);margin-bottom:16px;font-size:12px">PREVIEW</h4>
                    <div style="background:var(--bg-secondary);border:1px solid var(--border);border-radius:12px;padding:20px;display:inline-block">
                        ${currentLogo ? `<img src="${esc(currentLogo)}" style="max-width:120px;max-height:60px;margin-bottom:12px;display:block;margin-left:auto;margin-right:auto" id="brand-preview-img">` : `<svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="var(--accent)" stroke-width="1.5" style="margin-bottom:12px"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/><polyline points="9 12 11 14 15 10"/></svg>`}
                        <div style="font-size:18px;font-weight:700;color:var(--accent)" id="brand-preview-name">${esc(currentName)}</div>
                        <div style="font-size:11px;color:var(--text-muted);margin-top:2px" id="brand-preview-tagline">${esc(currentTagline)}</div>
                    </div>
                </div>
            </div>
        </div>
        <div class="card" style="padding:20px">
            <h4 style="margin:0 0 12px;font-size:14px">Report Branding</h4>
            <p style="color:var(--text-secondary);font-size:13px">The company name and logo will appear on all generated reports (HTML and PDF) and email notifications sent to your clients.</p>
        </div>`;

    // Live preview
    document.getElementById('brand-name')?.addEventListener('input', e => {
        const el = document.getElementById('brand-preview-name');
        if (el) el.textContent = e.target.value || 'PC Plus Computing';
    });
    document.getElementById('brand-tagline')?.addEventListener('input', e => {
        const el = document.getElementById('brand-preview-tagline');
        if (el) el.textContent = e.target.value || 'Endpoint Protection';
    });
}

async function uploadLogo() {
    const file = document.getElementById('brand-logo-file')?.files[0];
    if (!file) return;
    const formData = new FormData();
    formData.append('logo', file);
    const res = await apiFetch(API + '/branding/logo', { method: 'POST', body: formData });
    if (res?.ok) {
        const data = await res.json();
        document.getElementById('brand-logo-url').value = data.url || '';
        const img = document.getElementById('brand-preview-img');
        if (img) img.src = data.url;
        else loadBranding();
    }
}

async function saveBranding() {
    const data = {
        companyName: document.getElementById('brand-name').value,
        tagline: document.getElementById('brand-tagline').value,
        logoUrl: document.getElementById('brand-logo-url').value
    };
    const res = await apiFetch(API + '/branding', {
        method: 'POST', headers: {'Content-Type':'application/json'}, body: JSON.stringify(data)
    });
    if (res?.ok) {
        alert('Branding saved!');
        // Update sidebar branding
        const logo = document.querySelector('.sidebar-logo h1');
        const tagline = document.querySelector('.sidebar-logo span');
        if (logo) logo.textContent = data.companyName;
        if (tagline) tagline.textContent = data.tagline;
    }
}

// === REMOTE COMMANDS (enhanced device actions) ===
function showRemoteCommands(deviceId, hostname) {
    const modal = document.getElementById('device-modal');
    const content = document.getElementById('device-modal-content');
    content.innerHTML = `
        <h3>Remote Commands - ${esc(hostname)}</h3>
        <p style="color:var(--text-secondary);font-size:13px;margin-bottom:16px">Send a command to this endpoint. It will be picked up on the next heartbeat (within 30 seconds).</p>
        <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px">
            <button class="btn btn-primary" onclick="sendDeviceCommand('${esc(deviceId)}','rescan')" style="padding:16px;font-size:14px;flex-direction:column;display:flex;align-items:center;gap:6px">
                <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 12a9 9 0 1 1-6.219-8.56"/></svg>
                Run Security Scan
            </button>
            <button class="btn btn-secondary" onclick="sendDeviceCommand('${esc(deviceId)}','restart-service')" style="padding:16px;font-size:14px;flex-direction:column;display:flex;align-items:center;gap:6px">
                <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="23 4 23 10 17 10"/><path d="M20.49 15a9 9 0 1 1-2.12-9.36L23 10"/></svg>
                Restart Service
            </button>
            <button class="btn btn-secondary" onclick="sendDeviceCommand('${esc(deviceId)}','update-agent')" style="padding:16px;font-size:14px;flex-direction:column;display:flex;align-items:center;gap:6px">
                <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="7 10 12 15 17 10"/><line x1="12" y1="15" x2="12" y2="3"/></svg>
                Update Agent
            </button>
            <button class="btn btn-danger" onclick="if(confirm('This will lock down the device. Continue?'))sendDeviceCommand('${esc(deviceId)}','lockdown')" style="padding:16px;font-size:14px;flex-direction:column;display:flex;align-items:center;gap:6px">
                <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="3" y="11" width="18" height="11" rx="2" ry="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/></svg>
                Lockdown Device
            </button>
            <button class="btn btn-secondary" onclick="sendDeviceCommand('${esc(deviceId)}','collect-inventory')" style="padding:16px;font-size:14px;flex-direction:column;display:flex;align-items:center;gap:6px">
                <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg>
                Collect Inventory
            </button>
            <button class="btn btn-secondary" onclick="sendDeviceCommand('${esc(deviceId)}','collect-bitlocker')" style="padding:16px;font-size:14px;flex-direction:column;display:flex;align-items:center;gap:6px">
                <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="3" y="11" width="18" height="11" rx="2" ry="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/><circle cx="12" cy="16" r="1"/></svg>
                Get BitLocker Keys
            </button>
        </div>
        <div style="margin-top:16px;text-align:right">
            <button class="btn btn-secondary" onclick="document.getElementById('device-modal').classList.remove('active')">Close</button>
        </div>`;
    modal.classList.add('active');
}

async function sendDeviceCommand(deviceId, command) {
    try {
        const res = await apiFetch(API + '/devices/' + deviceId + '/command', {
            method: 'POST', headers: {'Content-Type':'application/json'},
            body: JSON.stringify({ command: command })
        });
        if (res?.ok) {
            alert('Command "' + command + '" sent! It will execute on the next heartbeat.');
            document.getElementById('device-modal').classList.remove('active');
        }
    } catch (err) { alert('Failed to send command'); }
}

// === COMPANY-WIDE REMOTE COMMANDS ===
function showCompanyCommands() {
    if (!currentCustomer) { alert('Please select a customer first.'); return; }
    const modal = document.getElementById('device-modal');
    const content = document.getElementById('device-modal-content');
    content.classList.remove('modal-wide');
    content.innerHTML = `
        <h3>Company Commands - ${esc(currentCustomer)}</h3>
        <p style="color:var(--text-secondary);font-size:13px;margin-bottom:16px">Send a command to ALL devices under <strong>${esc(currentCustomer)}</strong>. Commands execute on the next heartbeat (within 30 seconds).</p>
        <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px">
            <button class="btn btn-primary" onclick="sendCompanyCommand('rescan')" style="padding:16px;font-size:14px;display:flex;flex-direction:column;align-items:center;gap:6px">
                <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 12a9 9 0 1 1-6.219-8.56"/></svg>
                Run Security Scan (All)
            </button>
            <button class="btn btn-secondary" onclick="sendCompanyCommand('restart-service')" style="padding:16px;font-size:14px;display:flex;flex-direction:column;align-items:center;gap:6px">
                <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="23 4 23 10 17 10"/><path d="M20.49 15a9 9 0 1 1-2.12-9.36L23 10"/></svg>
                Restart Service (All)
            </button>
            <button class="btn btn-secondary" onclick="sendCompanyCommand('update-agent')" style="padding:16px;font-size:14px;display:flex;flex-direction:column;align-items:center;gap:6px">
                <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="7 10 12 15 17 10"/><line x1="12" y1="15" x2="12" y2="3"/></svg>
                Update Agent (All)
            </button>
            <button class="btn btn-secondary" onclick="sendCompanyCommand('collect-inventory')" style="padding:16px;font-size:14px;display:flex;flex-direction:column;align-items:center;gap:6px">
                <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg>
                Collect Inventory (All)
            </button>
            <button class="btn btn-secondary" onclick="sendCompanyCommand('collect-bitlocker')" style="padding:16px;font-size:14px;display:flex;flex-direction:column;align-items:center;gap:6px">
                <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="3" y="11" width="18" height="11" rx="2" ry="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/><circle cx="12" cy="16" r="1"/></svg>
                Get BitLocker Keys (All)
            </button>
            <button class="btn btn-danger" onclick="if(confirm('This will LOCKDOWN ALL devices under ${esc(currentCustomer)}. Are you sure?'))sendCompanyCommand('lockdown')" style="padding:16px;font-size:14px;display:flex;flex-direction:column;align-items:center;gap:6px">
                <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="3" y="11" width="18" height="11" rx="2" ry="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/></svg>
                Lockdown All Devices
            </button>
        </div>
        <div style="margin-top:16px;text-align:right">
            <button class="btn btn-secondary" onclick="document.getElementById('device-modal').classList.remove('active')">Close</button>
        </div>`;
    modal.classList.add('active');
}

async function sendCompanyCommand(command) {
    if (!currentCustomer) return;
    try {
        const res = await apiFetch(API + '/company/' + encodeURIComponent(currentCustomer) + '/command', {
            method: 'POST', headers: {'Content-Type':'application/json'},
            body: JSON.stringify({ command: command })
        });
        if (res?.ok) {
            const data = await res.json();
            alert('Command "' + command + '" sent to ' + data.deviceCount + ' devices! Will execute on next heartbeat.');
            document.getElementById('device-modal').classList.remove('active');
        }
    } catch (err) { alert('Failed to send company command'); }
}

// === REMEDIATION LIBRARY ===
const REMEDIATION_LIBRARY = [
    { id: 'cfa', name: 'Controlled Folder Access', category: 'Ransomware Protection', auto: true, desc: 'Enables Windows Controlled Folder Access to prevent unauthorized apps from modifying protected folders.' },
    { id: 'defender_rt', name: 'Real-time Protection', category: 'Protection', auto: true, desc: 'Enables Windows Defender real-time monitoring to catch threats as they appear.' },
    { id: 'firewall', name: 'Windows Firewall', category: 'Protection', auto: true, desc: 'Enables Windows Firewall on all network profiles (Domain, Private, Public).' },
    { id: 'rdp', name: 'Disable Remote Desktop', category: 'Network', auto: false, desc: 'Disables Remote Desktop Protocol. WARNING: Only apply if clients do NOT use RDP for remote access.' },
    { id: 'rdp_exposure', name: 'RDP Network Level Auth', category: 'Network', auto: true, desc: 'Enables NLA (Network Level Authentication) for RDP - does NOT disable RDP, just adds authentication requirement.' },
    { id: 'smbv1', name: 'Disable SMBv1', category: 'Network', auto: true, desc: 'Disables the vulnerable SMBv1 protocol used by WannaCry and similar ransomware.' },
    { id: 'uac', name: 'User Account Control', category: 'Protection', auto: true, desc: 'Enables UAC to prevent unauthorized system changes.' },
    { id: 'ps_logging', name: 'PowerShell Logging', category: 'EDR & Advanced', auto: true, desc: 'Enables Script Block Logging, Module Logging, and Transcription for PowerShell attack detection.' },
    { id: 'ps_exec_policy', name: 'Script Execution Policy', category: 'EDR & Advanced', auto: true, desc: 'Sets PowerShell execution policy to RemoteSigned to prevent running unsigned scripts.' },
    { id: 'guest', name: 'Disable Guest Account', category: 'Identity & Access', auto: true, desc: 'Disables the built-in Guest account to prevent unauthorized access.' },
    { id: 'autologin', name: 'Disable Auto-Login', category: 'Identity & Access', auto: true, desc: 'Removes stored auto-login credentials from the registry.' },
    { id: 'shadow_copies', name: 'Enable Shadow Copies', category: 'Ransomware Protection', auto: true, desc: 'Enables System Restore and Volume Shadow Copy Service for file recovery.' },
    { id: 'asr_rules', name: 'Attack Surface Reduction', category: 'EDR & Advanced', auto: true, desc: 'Enables 10 ASR rules to block common attack vectors (Office macros, scripts, credential theft).' },
    { id: 'lsass_protect', name: 'LSASS Protection', category: 'Identity & Access', auto: true, desc: 'Enables Protected Process Light (PPL) for LSASS to prevent credential dumping.' },
    { id: 'dns_security', name: 'Secure DNS', category: 'Network', auto: true, desc: 'Configures DNS to use Quad9 (9.9.9.9) secure resolvers with built-in threat blocking.' },
    { id: 'account_lockout', name: 'Account Lockout Policy', category: 'Identity & Access', auto: true, desc: 'Sets account lockout after 5 failed attempts with 30-minute reset window.' },
    { id: 'llmnr_netbios', name: 'Disable LLMNR/NetBIOS', category: 'Network', auto: true, desc: 'Disables LLMNR and NetBIOS name resolution to prevent relay attacks.' },
    { id: 'smartscreen', name: 'Enable SmartScreen', category: 'Endpoint Hardening', auto: true, desc: 'Enables Windows SmartScreen to warn about unrecognized apps and websites.' },
    { id: 'usb_storage', name: 'Block USB Storage', category: 'Device Control', auto: false, desc: 'Blocks USB mass storage devices. WARNING: Only apply if clients do NOT use USB drives.' },
    { id: 'office_macros', name: 'Block Office Macros', category: 'Endpoint Hardening', auto: true, desc: 'Blocks macro execution in Office documents downloaded from the internet.' },
    { id: 'tamper_protect', name: 'Tamper Protection', category: 'Protection', auto: false, desc: 'Must be enabled manually in Windows Security settings or via Intune/GPO.' },
    { id: 'bitlocker', name: 'BitLocker Encryption', category: 'Data Protection', auto: false, desc: 'Requires manual setup - needs recovery key backup and TPM configuration.' },
    { id: 'backup', name: 'Backup Configuration', category: 'Data Protection', auto: false, desc: 'Requires manual setup of backup solution (Windows Backup, third-party, or cloud).' },
    { id: 'edr', name: 'EDR / Advanced Protection', category: 'EDR & Advanced', auto: false, desc: 'Requires manual installation of an EDR solution (CrowdStrike, SentinelOne, etc.).' },
    { id: 'secure_boot', name: 'Secure Boot & TPM', category: 'Hardware Security', auto: false, desc: 'Requires BIOS/UEFI configuration - must be done physically on the machine.' }
];

function showRemediationLibrary() {
    if (!currentCustomer) { alert('Please select a customer first.'); return; }
    const modal = document.getElementById('device-modal');
    const content = document.getElementById('device-modal-content');
    content.classList.add('modal-wide');

    const categories = {};
    REMEDIATION_LIBRARY.forEach(r => {
        if (!categories[r.category]) categories[r.category] = [];
        categories[r.category].push(r);
    });

    let html = `
        <h3 style="margin-bottom:4px">Remediation Library</h3>
        <p style="color:var(--text-secondary);font-size:13px;margin-bottom:16px">Fix security issues across ALL devices under <strong>${esc(currentCustomer)}</strong>. Auto-fix items run remotely within 30 seconds.</p>
        <div style="max-height:70vh;overflow-y:auto;padding-right:8px">`;

    Object.entries(categories).forEach(([cat, items]) => {
        html += `<div style="margin-bottom:16px">
            <div style="font-weight:700;font-size:13px;color:var(--text-primary);padding:8px 0 6px;border-bottom:1px solid var(--border)">${esc(cat)}</div>`;
        items.forEach(r => {
            html += `<div style="display:flex;align-items:center;gap:12px;padding:10px 4px;border-bottom:1px solid rgba(255,255,255,0.05)">
                <div style="flex:1;min-width:0">
                    <div style="font-size:13px;font-weight:600;color:var(--text-primary)">${esc(r.name)}</div>
                    <div style="font-size:11px;color:var(--text-muted);margin-top:2px">${esc(r.desc)}</div>
                </div>
                ${r.auto
                    ? `<button class="btn btn-sm" onclick="sendCompanyRemediation('${r.id}','${esc(r.name)}')" style="background:#16a34a;color:#fff;padding:4px 14px;font-size:11px;white-space:nowrap;flex-shrink:0">Fix All</button>`
                    : `<span style="font-size:10px;color:var(--text-muted);padding:4px 10px;border:1px solid var(--border);border-radius:4px;white-space:nowrap;flex-shrink:0">Manual</span>`
                }
            </div>`;
        });
        html += `</div>`;
    });

    html += `</div>
        <div style="margin-top:16px;text-align:right">
            <button class="btn btn-secondary" onclick="document.getElementById('device-modal').classList.remove('active')">Close</button>
        </div>`;

    content.innerHTML = html;
    modal.classList.add('active');
}

async function sendCompanyRemediation(checkId, checkName) {
    if (!currentCustomer) return;
    if (!confirm('Apply "' + checkName + '" fix to ALL devices under ' + currentCustomer + '?')) return;
    try {
        const res = await apiFetch(API + '/company/' + encodeURIComponent(currentCustomer) + '/remediate', {
            method: 'POST', headers: {'Content-Type':'application/json'},
            body: JSON.stringify({ checkId: checkId })
        });
        if (res?.ok) {
            const data = await res.json();
            alert('"' + checkName + '" fix queued for ' + data.deviceCount + ' devices! Will apply on next heartbeat and auto-rescan.');
        }
    } catch (err) { alert('Failed to queue remediation'); }
}
