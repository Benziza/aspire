// Pointer and keyboard resizing for the resource list on the left of the resources layout.
//
// The width is applied to the layout element as the --resource-pane-expanded-width custom property while dragging, so
// resizing doesn't wait for a server round trip. The final width is sent to .NET, which persists it and renders the
// same value, so later renders don't reset it.
//
// Pointer capture is used so the drag survives the pointer leaving the narrow handle.

const registrations = new WeakMap();

export function registerPaneResizer(layoutElement, dotNetRef, minimumWidth, maximumWidth) {
    unregisterPaneResizer(layoutElement);

    const pane = layoutElement.querySelector(':scope > .resource-pane');
    const handle = pane?.querySelector(':scope > .resource-pane-resizer');
    if (!pane || !handle) {
        return;
    }

    let pointerId = null;
    let startX = 0;
    let startWidth = 0;
    let width = Math.round(pane.getBoundingClientRect().width);
    let disposed = false;

    // The pane never takes more than half of the window, so the content next to it stays usable.
    const bounds = () => {
        const max = Math.max(minimumWidth, Math.min(maximumWidth, Math.floor(window.innerWidth / 2)));
        return { min: minimumWidth, max };
    };

    const resizeTo = requestedWidth => {
        const { min, max } = bounds();
        const nextWidth = Math.max(min, Math.min(max, Math.round(requestedWidth)));
        if (nextWidth === width) {
            return false;
        }
        width = nextWidth;
        layoutElement.style.setProperty('--resource-pane-expanded-width', `${width}px`);
        handle.setAttribute('aria-valuenow', `${width}`);
        return true;
    };

    const commit = () => {
        if (disposed) {
            return;
        }
        dotNetRef.invokeMethodAsync('SetPaneWidthAsync', width).catch(error => {
            if (!disposed) {
                console.error('Failed to save the resource list width.', error);
            }
        });
    };

    const onPointerDown = e => {
        if (e.button !== 0 || !e.isPrimary) {
            return;
        }
        pointerId = e.pointerId;
        startX = e.clientX;
        startWidth = Math.round(pane.getBoundingClientRect().width);
        width = startWidth;
        handle.setPointerCapture(e.pointerId);
        handle.classList.add('resizing');
        document.body.style.cursor = 'col-resize';
        document.body.style.userSelect = 'none';
        e.preventDefault();
    };

    const onPointerMove = e => {
        if (pointerId !== e.pointerId) {
            return;
        }
        resizeTo(startWidth + e.clientX - startX);
    };

    const onPointerEnd = e => {
        if (pointerId !== e.pointerId) {
            return;
        }
        pointerId = null;
        if (handle.hasPointerCapture(e.pointerId)) {
            handle.releasePointerCapture(e.pointerId);
        }
        handle.classList.remove('resizing');
        document.body.style.cursor = '';
        document.body.style.userSelect = '';
        if (width !== startWidth) {
            commit();
        }
    };

    // Follow the window splitter pattern: https://www.w3.org/WAI/ARIA/apg/patterns/windowsplitter/.
    // The pane is on the left of the separator, so moving the separator right makes it wider.
    const onKeyDown = e => {
        if (e.ctrlKey || e.altKey || e.metaKey || e.isComposing) {
            return;
        }
        const step = e.shiftKey ? 50 : 10;
        const { min, max } = bounds();
        let nextWidth;
        switch (e.key) {
            case 'ArrowLeft':
                nextWidth = width - step;
                break;
            case 'ArrowRight':
                nextWidth = width + step;
                break;
            case 'Home':
                nextWidth = min;
                break;
            case 'End':
                nextWidth = max;
                break;
            default:
                return;
        }
        e.preventDefault();
        e.stopPropagation();
        if (resizeTo(nextWidth)) {
            commit();
        }
    };

    // Double-clicking the handle restores the default width.
    const onDoubleClick = e => {
        e.preventDefault();
        layoutElement.style.removeProperty('--resource-pane-expanded-width');
        const defaultWidth = Math.round(pane.getBoundingClientRect().width);
        if (resizeTo(defaultWidth) || defaultWidth === width) {
            commit();
        }
    };

    handle.addEventListener('pointerdown', onPointerDown);
    handle.addEventListener('pointermove', onPointerMove);
    handle.addEventListener('pointerup', onPointerEnd);
    handle.addEventListener('pointercancel', onPointerEnd);
    handle.addEventListener('keydown', onKeyDown);
    handle.addEventListener('dblclick', onDoubleClick);

    registrations.set(layoutElement, () => {
        disposed = true;
        handle.removeEventListener('pointerdown', onPointerDown);
        handle.removeEventListener('pointermove', onPointerMove);
        handle.removeEventListener('pointerup', onPointerEnd);
        handle.removeEventListener('pointercancel', onPointerEnd);
        handle.removeEventListener('keydown', onKeyDown);
        handle.removeEventListener('dblclick', onDoubleClick);
        if (pointerId !== null) {
            document.body.style.cursor = '';
            document.body.style.userSelect = '';
        }
    });
}

export function unregisterPaneResizer(layoutElement) {
    if (!layoutElement) {
        return;
    }
    const unregister = registrations.get(layoutElement);
    if (unregister) {
        registrations.delete(layoutElement);
        unregister();
    }
}
