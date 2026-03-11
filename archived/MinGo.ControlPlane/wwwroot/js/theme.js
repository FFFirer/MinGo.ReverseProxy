window.theme = {
    init: function () {
        const savedTheme = localStorage.getItem('theme');
        const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
        
        if (savedTheme === 'dark' || (!savedTheme && prefersDark)) {
            document.documentElement.classList.add('dark');
            return true;
        }
        return false;
    },
    
    toggle: function () {
        const isDark = document.documentElement.classList.toggle('dark');
        localStorage.setItem('theme', isDark ? 'dark' : 'light');
        return isDark;
    },
    
    isDarkMode: function () {
        return document.documentElement.classList.contains('dark');
    }
};
