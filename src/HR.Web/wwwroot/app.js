function setTheme(isDark) {    
    document.getElementsByTagName('body')[0].style.display = 'none';
    let darkLink = document.getElementById('sf-dark-theme');
    console.log(darkLink);
    darkLink.disabled = !isDark;
    setTimeout(function () { document.getElementsByTagName('body')[0].style.display = 'block'; }, 300);
}
