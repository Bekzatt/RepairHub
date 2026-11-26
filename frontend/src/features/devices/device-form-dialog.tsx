import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { FormAlert } from '@/components/form-field'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import type { Device } from '@/features/customers/types'
import { errorMessage } from '@/lib/form-errors'
import { t } from '@/locales'
import { useCreateDevice, useUpdateDevice } from './api'
import { DeviceFields, deviceSchema, emptyDevice, toDeviceInput, type DeviceValues } from './device-fields'

interface DeviceFormDialogProps {
  /** Edit this device, or create one for `customerId` when null. */
  device: Device | null
  customerId: string
  customerName?: string
  open: boolean
  onOpenChange: (open: boolean) => void
  onSaved: (device: Device, created: boolean) => void
}

export function DeviceFormDialog({ device, customerId, customerName, open, onOpenChange, onSaved }: DeviceFormDialogProps) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>{device ? t.devices.form.editTitle : t.devices.form.createTitle}</DialogTitle>
          {customerName && <DialogDescription>{customerName}</DialogDescription>}
        </DialogHeader>
        <DeviceForm device={device} customerId={customerId} onClose={() => onOpenChange(false)} onSaved={onSaved} />
      </DialogContent>
    </Dialog>
  )
}

function DeviceForm({
  device,
  customerId,
  onClose,
  onSaved,
}: {
  device: Device | null
  customerId: string
  onClose: () => void
  onSaved: (device: Device, created: boolean) => void
}) {
  const create = useCreateDevice()
  const update = useUpdateDevice()
  const [formError, setFormError] = useState<string | null>(null)
  const form = useForm<DeviceValues>({
    resolver: zodResolver(deviceSchema),
    defaultValues: device
      ? {
          type: device.type,
          brand: device.brand,
          model: device.model,
          serialNumber: device.serialNumber ?? '',
          imei: device.imei ?? '',
          description: device.description ?? '',
        }
      : emptyDevice,
  })

  async function onSubmit(values: DeviceValues) {
    setFormError(null)
    try {
      const input = toDeviceInput(values)
      const saved = device ? await update.mutateAsync({ id: device.id, ...input }) : await create.mutateAsync({ ...input, customerId })
      onSaved(saved, !device)
      onClose()
    } catch (error) {
      setFormError(errorMessage(error))
    }
  }

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} noValidate className="space-y-4">
      {formError && <FormAlert>{formError}</FormAlert>}
      <DeviceFields form={form} />
      <DialogFooter>
        <Button type="button" variant="outline" onClick={onClose}>
          {t.common.cancel}
        </Button>
        <Button type="submit" disabled={form.formState.isSubmitting}>
          {form.formState.isSubmitting ? t.common.saving : device ? t.common.save : t.devices.form.create}
        </Button>
      </DialogFooter>
    </form>
  )
}
