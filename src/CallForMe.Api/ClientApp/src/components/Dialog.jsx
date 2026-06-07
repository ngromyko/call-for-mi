import { useEffect, useRef } from "react";

export function Dialog({ className = "modal", open, children, onCancel }) {
  const ref = useRef(null);

  useEffect(() => {
    const dialog = ref.current;
    if (!dialog) return;

    if (open && !dialog.open) {
      dialog.showModal();
    } else if (!open && dialog.open) {
      dialog.close();
    }
  }, [open]);

  return (
    <dialog
      ref={ref}
      className={className}
      onCancel={event => {
        if (onCancel) {
          event.preventDefault();
          onCancel();
        }
      }}
      onClose={() => {
        if (open) onCancel?.();
      }}
    >
      {children}
    </dialog>
  );
}

export function Icon({ children, className = "", ...props }) {
  return <span className={`material-symbols-rounded ${className}`.trim()} {...props}>{children}</span>;
}
