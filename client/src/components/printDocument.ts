/**
 * Handing a rendered document to the browser's print dialog.
 *
 * Its own module rather than an export beside the print component: a file that exports both a
 * component and a plain function loses React Fast Refresh for the whole file.
 */
/**
 * Hands the rendered document to the browser's print dialog.
 *
 * The two frames of delay are not superstition: React has to commit the document and the layout
 * has to settle before print() snapshots the page, or the PDF comes out with the previous
 * contents or nothing at all.
 */
export function printRenderedDocument(): Promise<void> {
  return new Promise((resolve) => {
    requestAnimationFrame(() => {
      requestAnimationFrame(() => {
        window.print()
        resolve()
      })
    })
  })
}
