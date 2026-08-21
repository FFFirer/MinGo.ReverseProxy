import { createSignal } from 'solid-js';

const [isOpen, setIsOpen] = createSignal(false);

export { isOpen };

export function toggleSidebar() {
  setIsOpen(prev => !prev);
}

export function closeSidebar() {
  setIsOpen(false);
}

export function initSidebar() {
  const handleResize = () => {
    setIsOpen(window.innerWidth >= 768);
  };
  handleResize();
  window.addEventListener('resize', handleResize);
}
