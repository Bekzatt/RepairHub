import { Controller, get, type FieldErrors, type FieldValues, type Path, type UseFormReturn } from 'react-hook-form'
import { z } from 'zod'
import { FormField } from '@/components/form-field'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import type { DeviceInput } from '@/features/customers/types'
import { DEVICE_TYPES } from '@/features/repair-orders/types'
import { imeiSchema, optionalText, orNull, requiredText } from '@/lib/validation'
import { t } from '@/locales'

export const deviceSchema = z.object({
  type: z.enum(DEVICE_TYPES, { error: t.validation.required }),
  brand: requiredText(100),
  model: requiredText(150),
  serialNumber: optionalText(100),
  imei: imeiSchema,
  description: optionalText(1000),
})
export type DeviceValues = z.infer<typeof deviceSchema>

export const emptyDevice: DeviceValues = { type: 'Laptop', brand: '', model: '', serialNumber: '', imei: '', description: '' }

export function toDeviceInput(values: DeviceValues): DeviceInput {
  return {
    type: values.type,
    brand: values.brand.trim(),
    model: values.model.trim(),
    serialNumber: orNull(values.serialNumber),
    imei: orNull(values.imei),
    description: orNull(values.description),
  }
}

/**
 * The device fields inside any form: at the root of the form values (device dialog), or nested under
 * `name` (the new-order form keeps them under "newDevice").
 */
export function DeviceFields<T extends FieldValues>({
  form,
  name,
  showDescription = true,
}: {
  form: UseFormReturn<T>
  name?: string
  showDescription?: boolean
}) {
  const path = (field: keyof DeviceValues) => (name ? `${name}.${field}` : field) as Path<T>
  const errors = (name ? get(form.formState.errors, name) : form.formState.errors) as FieldErrors<DeviceValues> | undefined
  const { register, control } = form

  return (
    <div className="space-y-4">
      <div className="grid gap-4 sm:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_minmax(0,1.3fr)]">
        <FormField label={t.devices.form.type} error={errors?.type?.message}>
          {(p) => (
            <Controller
              control={control}
              name={path('type')}
              render={({ field }) => (
                <Select value={field.value as string} onValueChange={field.onChange}>
                  <SelectTrigger {...p} className="w-full">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {DEVICE_TYPES.map((type) => (
                      <SelectItem key={type} value={type}>
                        {t.deviceTypes[type]}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            />
          )}
        </FormField>
        <FormField label={t.devices.form.brand} error={errors?.brand?.message}>
          {(p) => <Input {...p} autoComplete="off" placeholder={t.devices.form.brandPlaceholder} {...register(path('brand'))} />}
        </FormField>
        <FormField label={t.devices.form.model} error={errors?.model?.message}>
          {(p) => <Input {...p} autoComplete="off" {...register(path('model'))} />}
        </FormField>
      </div>
      <div className="grid gap-4 sm:grid-cols-2">
        <FormField label={t.devices.form.serialNumber} optionalLabel={t.common.optional} error={errors?.serialNumber?.message}>
          {(p) => <Input {...p} autoComplete="off" spellCheck={false} {...register(path('serialNumber'))} />}
        </FormField>
        <FormField label={t.devices.form.imei} optionalLabel={t.common.optional} error={errors?.imei?.message}>
          {(p) => <Input {...p} inputMode="numeric" autoComplete="off" {...register(path('imei'))} />}
        </FormField>
      </div>
      {showDescription && (
        <FormField label={t.devices.form.description} optionalLabel={t.common.optional} error={errors?.description?.message}>
          {(p) => <Input {...p} placeholder={t.devices.form.descriptionPlaceholder} {...register(path('description'))} />}
        </FormField>
      )}
    </div>
  )
}
