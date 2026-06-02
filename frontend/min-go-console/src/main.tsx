import { render } from 'solid-js/web';
import App from './App';
import { initTheme } from './store/theme';
import './styles/app.css';

initTheme();

const root = document.getElementById('app');
if (root) {
  render(() => <App />, root);
}
