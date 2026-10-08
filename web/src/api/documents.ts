import type { DocumentItem } from '../types'
import { api, apiBlob } from './client'

export const ALLOWED_EXTENSIONS = ['pdf', 'docx', 'xlsx', 'png', 'jpg', 'jpeg']
export const MAX_BYTES = 10 * 1024 * 1024

const base = (automationId: string) => `/automations/${automationId}/documents`

export const listDocuments = (automationId: string, signal?: AbortSignal) =>
  api<DocumentItem[]>(base(automationId), { signal })

export function uploadDocument(automationId: string, file: File) {
  const form = new FormData()
  form.append('file', file)
  return api<DocumentItem>(base(automationId), { method: 'POST', body: form })
}

export const deleteDocument = (automationId: string, documentId: string) =>
  api<void>(`${base(automationId)}/${documentId}`, { method: 'DELETE' })

export async function downloadDocument(automationId: string, doc: DocumentItem) {
  const blob = await apiBlob(`${base(automationId)}/${doc.id}/download`)
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = doc.originalFileName
  a.click()
  URL.revokeObjectURL(url)
}

/** Client-side pre-check for fast feedback; the server is the real gate. */
export function precheck(file: File): string | null {
  const ext = file.name.split('.').pop()?.toLowerCase() ?? ''
  if (!ALLOWED_EXTENSIONS.includes(ext)) return `Unsupported type. Allowed: ${ALLOWED_EXTENSIONS.join(', ').toUpperCase()}`
  if (file.size === 0) return 'The file is empty'
  if (file.size > MAX_BYTES) return 'Exceeds the 10 MB limit'
  return null
}
