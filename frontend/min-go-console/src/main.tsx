import { render } from 'solid-js/web';
import App from './App';
import { initTheme } from './store/theme';
import { initSidebar } from './store/sidebar';
import './styles/app.css';

initTheme();
initSidebar();

const root = document.getElementById('app');
if (root) {
  render(() => <App />, root);
}
