// The ECharts parts the portal uses, imported by name so the bundler keeps only them (the package barrels
// `echarts/charts` and `echarts/components` hold every chart type). Loaded lazily by the chart components.
import { GaugeChart, LineChart, PieChart } from 'echarts/charts';
import { GridComponent, LegendComponent, TooltipComponent } from 'echarts/components';
import { init, use } from 'echarts/core';
import { CanvasRenderer } from 'echarts/renderers';

use([LineChart, GaugeChart, PieChart, GridComponent, TooltipComponent, LegendComponent, CanvasRenderer]);

export { init };
