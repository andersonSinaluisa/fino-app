import type { Category } from '../../types/api';

/**
 * §10 ("categorización automática") y §43 ("NO inventar categorías").
 *
 * El parser propone un CÓDIGO de categoría ("COMIDA"). Este archivo lo convierte en
 * una categoría real de esta persona -- o en nada. Nunca crea una categoría, nunca
 * devuelve un nombre que no exista ya en su cuenta.
 *
 * Devolver `null` no es un fallo: es la respuesta correcta cuando la palabra clave
 * no corresponde a ninguna categoría suya. En ese caso el cliente manda el
 * movimiento SIN categoría y decide el motor de reglas del servidor, que es el
 * único que conoce las reglas que esta persona ha ido enseñando a Fino (§10,
 * prioridades 1 y 2). El diccionario local es la prioridad 3, no la primera.
 */

/**
 * Alias de nombre → código del catálogo, para las categorías propias que la persona
 * creó a mano. Una categoría suya llamada "Alimentación" debería reconocerse cuando
 * el parser dice COMIDA, aunque su código generado sea "ALIMENTACION".
 *
 * Deliberadamente corto: son sinónimos evidentes, no un intento de adivinar
 * cualquier nombre que alguien pueda inventar.
 */
const NAME_ALIASES: Readonly<Record<string, readonly string[]>> = {
  COMIDA: ['comida', 'alimentacion', 'alimentación', 'restaurantes', 'comidas', 'food'],
  TRANSPORTE: ['transporte', 'movilizacion', 'movilización', 'transportes'],
  SALUD: ['salud', 'medicina', 'medico', 'médico'],
  SUPERMERCADO: ['supermercado', 'mercado', 'despensa', 'viveres', 'víveres'],
  COMPRAS: ['compras', 'shopping'],
  SERVICIOS: ['servicios', 'servicios basicos', 'servicios básicos', 'basicos', 'básicos'],
  ENTRETENIMIENTO: ['entretenimiento', 'ocio', 'diversion', 'diversión'],
  EDUCACION: ['educacion', 'educación', 'estudios'],
  INGRESOS: ['ingresos', 'ingreso', 'sueldo', 'salario'],
};

function fold(value: string): string {
  return value.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase().trim();
}

/**
 * Busca la categoría de la persona que corresponde al código propuesto.
 *
 * El orden es el que respeta lo que la persona ha hecho: primero sus categorías
 * propias (si alguien se creó su propia "Comida", es esa la que quiere ver, no la
 * del sistema), luego las del sistema.
 */
export function resolveCategory(
  categoryCode: string | null,
  categories: readonly Category[] | undefined,
): Category | null {
  if (!categoryCode || !categories || categories.length === 0) {
    return null;
  }

  const code = categoryCode.toUpperCase();
  const aliases = NAME_ALIASES[code] ?? [];

  const matches = (category: Category): boolean => {
    if (category.code?.toUpperCase() === code) {
      return true;
    }

    return aliases.includes(fold(category.name));
  };

  const own = categories.find((category) => !category.isSystem && matches(category));
  if (own) {
    return own;
  }

  return categories.find((category) => category.isSystem && matches(category)) ?? null;
}

/**
 * §17-19: cuando se toca un frecuente, la categoría viene con él. Este ayudante
 * solo la busca por id para poder pintar su ícono y su color en la vista previa;
 * si la categoría se borró entre medias, devuelve null y la vista previa dice
 * "Sin categoría" en vez de romperse.
 */
export function findCategoryById(
  categoryId: string | null | undefined,
  categories: readonly Category[] | undefined,
): Category | null {
  if (!categoryId || !categories) {
    return null;
  }

  return categories.find((category) => category.id === categoryId) ?? null;
}
