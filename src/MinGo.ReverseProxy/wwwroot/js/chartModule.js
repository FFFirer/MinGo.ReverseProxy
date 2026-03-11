// Chart.js 模块

// 图表实例
let requestsChart = null;

/**
 * 初始化图表
 * @param {HTMLElement} canvas - 画布元素
 * @param {Array} labels - 时间标签数组
 * @param {Array} counts - 总请求数数组
 * @param {Array} errorCounts - 错误请求数数组
 * @returns {Object} 图表引用对象
 */
export function initChart(canvas, labels, counts, errorCounts) {
    console.log('Initializing chart with data:');
    console.log('Labels:', labels);
    console.log('Counts:', counts);
    console.log('Error counts:', errorCounts);
    console.log('Canvas element:', canvas);
    
    if (!canvas) {
        console.error('Canvas element not found');
        return null;
    }
    
    const ctx2d = canvas.getContext('2d');
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
    if (requestsChart) {
        console.log('Destroying old chart');
        requestsChart.destroy();
    }
    
    console.log('Creating new chart');
    requestsChart = new Chart(ctx2d, {
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
    
    // 返回一个简化的对象，避免循环引用
    return { 
        id: 'requestsChart' 
    };
}

/**
 * 更新图表数据
 * @param {Object} chartRef - 图表引用对象
 * @param {Array} labels - 时间标签数组
 * @param {Array} counts - 总请求数数组
 * @param {Array} errorCounts - 错误请求数数组
 * @returns {boolean} 更新是否成功
 */
export function updateChartData(chartRef, labels, counts, errorCounts) {
    console.log('Updating chart data:');
    console.log('Chart ref:', chartRef);
    console.log('Labels:', labels);
    console.log('Counts:', counts);
    console.log('Error counts:', errorCounts);
    
    if (!requestsChart) {
        console.error('Chart instance not found');
        return false;
    }
    
    // 更新数据
    requestsChart.data.labels = labels;
    requestsChart.data.datasets[0].data = counts;
    requestsChart.data.datasets[1].data = errorCounts;
    
    // 更新图表
    requestsChart.update();
    console.log('Chart updated successfully');
    return true;
}
