import { useCallback, useEffect, useRef, useState, type DragEvent } from 'react'
import { deleteDocument, downloadDocument, listDocuments, precheck, uploadDocument } from '../api/documents'
import { formatBytes, formatDate } from '../lib/format'
import type { DocumentItem } from '../types'

interface UploadStatus {
  key: string
  name: string
  state: 'uploading' | 'done' | 'error'
  error?: string
}

export default function DocumentsSection({ automationId, canEdit }: { automationId: string; canEdit: boolean }) {
  const [docs, setDocs] = useState<DocumentItem[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [uploads, setUploads] = useState<UploadStatus[]>([])
  const [dragging, setDragging] = useState(false)
  const inputRef = useRef<HTMLInputElement>(null)

  const reload = useCallback(
    (signal?: AbortSignal) =>
      listDocuments(automationId, signal)
        .then((d) => {
          setDocs(d)
          setLoadError(null)
        })
        .catch((e: Error) => e.name !== 'AbortError' && setLoadError(e.message)),
    [automationId],
  )

  useEffect(() => {
    const ctrl = new AbortController()
    reload(ctrl.signal)
    return () => ctrl.abort()
  }, [reload])

  async function uploadAll(files: File[]) {
    setActionError(null)
    const batch = files.map((f, i) => ({ file: f, key: `${Date.now()}-${i}-${f.name}` }))
    setUploads((u) => [...u, ...batch.map((b) => ({ key: b.key, name: b.file.name, state: 'uploading' as const }))])

    const patch = (key: string, p: Partial<UploadStatus>) =>
      setUploads((u) => u.map((x) => (x.key === key ? { ...x, ...p } : x)))

    await Promise.all(
      batch.map(async ({ file, key }) => {
        const problem = precheck(file)
        if (problem) return patch(key, { state: 'error', error: problem })
        try {
          await uploadDocument(automationId, file)
          patch(key, { state: 'done' })
        } catch (e) {
          patch(key, { state: 'error', error: firstError(e) })
        }
      }),
    )
    await reload()
    // Successful rows disappear once the list shows them; failed ones stay until dismissed.
    setUploads((u) => u.filter((x) => x.state === 'error'))
  }

  function onPick(files: FileList | null) {
    if (files && files.length > 0) void uploadAll(Array.from(files))
    if (inputRef.current) inputRef.current.value = ''
  }

  function onDrop(e: DragEvent) {
    e.preventDefault()
    setDragging(false)
    onPick(e.dataTransfer.files)
  }

  async function onDelete(doc: DocumentItem) {
    if (!window.confirm(`Delete "${doc.originalFileName}"? This cannot be undone.`)) return
    setActionError(null)
    try {
      await deleteDocument(automationId, doc.id)
      await reload()
    } catch (e) {
      setActionError(firstError(e))
    }
  }

  async function onDownload(doc: DocumentItem) {
    setActionError(null)
    try {
      await downloadDocument(automationId, doc)
    } catch (e) {
      setActionError(firstError(e))
    }
  }

  return (
    <section className="mt-6 rounded-lg border border-gray-200 bg-white p-6">
      <h2 className="text-lg font-semibold">Related documents</h2>

      {canEdit && (
        <div
          onDragOver={(e) => {
            e.preventDefault()
            setDragging(true)
          }}
          onDragLeave={() => setDragging(false)}
          onDrop={onDrop}
          className={`mt-4 rounded-md border-2 border-dashed p-4 text-center text-sm ${
            dragging ? 'border-blue-500 bg-blue-50' : 'border-gray-300'
          }`}
        >
          <p className="text-gray-600">
            Drag files here or{' '}
            <button type="button" onClick={() => inputRef.current?.click()} className="text-blue-600 hover:underline">
              choose files
            </button>
          </p>
          <p className="mt-1 text-xs text-gray-400">PDF, DOCX, XLSX, PNG, JPG. Up to 10 MB each.</p>
          <input ref={inputRef} type="file" multiple hidden onChange={(e) => onPick(e.target.files)} />
        </div>
      )}

      {uploads.length > 0 && (
        <ul className="mt-3 space-y-1 text-sm">
          {uploads.map((u) => (
            <li key={u.key} className="flex items-center justify-between gap-2">
              <span className="truncate">{u.name}</span>
              {u.state === 'uploading' && <span className="text-gray-500">Uploading...</span>}
              {u.state === 'done' && <span className="text-green-600">Uploaded</span>}
              {u.state === 'error' && (
                <span className="flex items-center gap-2 text-red-600">
                  {u.error}
                  <button
                    onClick={() => setUploads((x) => x.filter((y) => y.key !== u.key))}
                    className="text-gray-400 hover:text-gray-700"
                    aria-label={`Dismiss ${u.name}`}
                  >
                    ×
                  </button>
                </span>
              )}
            </li>
          ))}
        </ul>
      )}

      {actionError && <p className="mt-3 text-sm text-red-600">{actionError}</p>}
      {loadError && <p className="mt-3 text-sm text-red-600">Could not load documents: {loadError}</p>}
      {!loadError && docs === null && <p className="mt-4 text-sm text-gray-500">Loading...</p>}
      {docs?.length === 0 && <p className="mt-4 text-sm text-gray-500">No documents yet.</p>}

      {docs && docs.length > 0 && (
        <ul className="mt-4 divide-y divide-gray-100">
          {docs.map((d) => (
            <li key={d.id} className="flex flex-wrap items-center justify-between gap-2 py-2">
              <div className="min-w-0">
                <p className="truncate text-sm font-medium">{d.originalFileName}</p>
                <p className="text-xs text-gray-500">
                  {formatBytes(d.sizeBytes)} · {d.uploadedBy} · {formatDate(d.uploadedAt)}
                </p>
              </div>
              <div className="flex gap-3 text-sm">
                <button onClick={() => onDownload(d)} className="text-blue-600 hover:underline">
                  Download
                </button>
                {canEdit && (
                  <button onClick={() => onDelete(d)} className="text-red-600 hover:underline">
                    Delete
                  </button>
                )}
              </div>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

function firstError(e: unknown): string {
  if (e instanceof Error && 'fieldErrors' in e) {
    const fe = (e as { fieldErrors: Record<string, string[]> }).fieldErrors
    const first = Object.values(fe)[0]?.[0]
    if (first) return first
  }
  return e instanceof Error ? e.message : 'Something went wrong'
}
