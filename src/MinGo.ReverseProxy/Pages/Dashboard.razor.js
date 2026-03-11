export function initChart(ctx, labels, counts, errorCounts) {
    console.log('Initializing chart with data:');
    console.log('Labels:', labels);
    console.log('Counts:', counts);
    console.log('Error counts:', errorCounts);
    console.log('Canvas element:', ctx);

    if (!ctx) {
        console.error('Canvas element not found');
        return null;
    }

    const ctx2d = ctx.getContext('2d');
    console.log('Canvas context:', ctx2d);

    if (!ctx2d) {
        console.error('Canvas context not found');
        return null;
    }

    // 检查Chart是否定义
    if (typeof Chart === 'undefined') {
        console.error('Chart.js not loaded');
        return null;
    }

    // 销毁旧图表
    if (window.requestsChart) {
        console.log('Destroying old chart');
        window.requestsChart.destroy();
    }

    console.log('Creating new chart');
    window.requestsChart = new Chart(ctx2d, {
        type: 'line',
        data: {
            labels: labels,
            datasets: [
                {
                    label: '总请求数',
                    data: counts,
                    borderColor: '#3b82f6',
                    backgroundColor: 'rgba(59, 130, 246, 0.1)',
                    tension: 0.4,
                    fill: true
                },
                {
                    label: '错误请求数',
                    data: errorCounts,
                    borderColor: '#ef4444',
                    backgroundColor: 'rgba(239, 68, 68, 0.1)',
                    tension: 0.4,
                    fill: true
                }
            ]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: {
                    position: 'top',
                },
                tooltip: {
                    mode: 'index',
                    intersect: false
                }
            },
            scales: {
                y: {
                    beginAtZero: true,
                    title: {
                        display: true,
                        text: '请求数'
                    }
                },
                x: {
                    title: {
                        display: true,
                        text: '时间'
                    }
                }
            }
        }
    });
    console.log('Chart created successfully');
    return window.requestsChart;
}

// 更新图表数据
export function updateChartData(chart, labels, counts, errorCounts) {
    console.log('Updating chart data:');
    console.log('Labels:', labels);
    console.log('Counts:', counts);
    console.log('Error counts:', errorCounts);

    if (!chart) {
        console.error('Chart instance not found');
        return false;
    }

    // 更新数据
    chart.data.labels = labels;
    chart.data.datasets[0].data = counts;
    chart.data.datasets[1].data = errorCounts;

    // 更新图表
    chart.update();
    console.log('Chart updated successfully');
    return true;
}