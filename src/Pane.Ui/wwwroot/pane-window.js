// Native-window glue for the launcher:
//   • report content height so the host can size the window to fit (Spotlight-style)
//   • keep the search input focused, and refocus it when the window regains focus
//   • tell the host to dismiss the overlay when the window loses focus
//
// `dotnet` is a DotNetObjectReference to the Launcher component exposing
// [JSInvokable] OnContentResized(int) and OnWindowBlur().

window.paneWindow = (function () {
    let observer = null;

    function searchInput() {
        return document.querySelector(".pane-search");
    }

    function focusSearch() {
        const el = searchInput();
        if (el && document.activeElement !== el) el.focus();
    }

    return {
        init: function (dotnet) {
            // 1. Content-driven window height.
            const report = () =>
                dotnet.invokeMethodAsync("OnContentResized", Math.ceil(document.body.scrollHeight));
            observer?.disconnect();
            observer = new ResizeObserver(report);
            observer.observe(document.body);
            report();

            // 2. Focus follows the window.
            window.addEventListener("focus", focusSearch);
            document.addEventListener("click", focusSearch);

            // 3. Dismiss on blur (window lost key status).
            window.addEventListener("blur", () => dotnet.invokeMethodAsync("OnWindowBlur"));

            focusSearch();
        },

        focus: focusSearch,
    };
})();
