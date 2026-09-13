import { findCategoryById, resolveCategory } from '../../utils/quickEntry/resolveCategory';
import type { Category } from '../../types/api';

/**
 * §10 y §43: la pista del parser se resuelve contra las categorías REALES de la
 * persona, y cuando no hay ninguna que encaje la respuesta correcta es null -- no
 * una categoría inventada.
 */

function category(overrides: Partial<Category> = {}): Category {
  return {
    id: 'cat-1',
    code: 'COMIDA',
    name: 'Comida',
    icon: 'utensils',
    color: '#E4A853',
    isSystem: true,
    isIncome: false,
    ...overrides,
  };
}

describe('resolveCategory', () => {
  it('encuentra la categoría del sistema por su código', () => {
    const categories = [category(), category({ id: 'cat-2', code: 'TRANSPORTE', name: 'Transporte' })];

    expect(resolveCategory('TRANSPORTE', categories)?.id).toBe('cat-2');
  });

  it('prefiere una categoría propia de la persona sobre la del sistema', () => {
    // Si alguien se creó su propia "Comida", es esa la que quiere ver en su app,
    // no la que trae Fino de fábrica.
    const categories = [
      category({ id: 'sistema', isSystem: true }),
      category({ id: 'mia', code: 'COMIDA', isSystem: false }),
    ];

    expect(resolveCategory('COMIDA', categories)?.id).toBe('mia');
  });

  it('reconoce una categoría propia con un nombre sinónimo', () => {
    // Su código generado sería "ALIMENTACION", que no coincide con COMIDA.
    const categories = [category({ id: 'mia', code: 'ALIMENTACION', name: 'Alimentación', isSystem: false })];

    expect(resolveCategory('COMIDA', categories)?.id).toBe('mia');
  });

  it('devuelve null cuando la persona no tiene esa categoría', () => {
    // Y eso NO es un fallo: el cliente manda el movimiento sin categoría y decide
    // el motor de reglas del servidor, que sí conoce las reglas de esta persona.
    expect(resolveCategory('SALUD', [category()])).toBeNull();
  });

  it('devuelve null sin pista, sin categorías, o con la lista vacía', () => {
    expect(resolveCategory(null, [category()])).toBeNull();
    expect(resolveCategory('COMIDA', undefined)).toBeNull();
    expect(resolveCategory('COMIDA', [])).toBeNull();
  });

  it('nunca inventa una categoría', () => {
    const result = resolveCategory('CATEGORIA_QUE_NO_EXISTE', [category()]);

    expect(result).toBeNull();
  });
});

describe('findCategoryById', () => {
  it('encuentra la categoría de un frecuente', () => {
    expect(findCategoryById('cat-1', [category()])?.name).toBe('Comida');
  });

  it('devuelve null si la categoría se borró entre medias', () => {
    // La vista previa dirá "Sin categoría" en vez de romperse.
    expect(findCategoryById('cat-borrada', [category()])).toBeNull();
    expect(findCategoryById(null, [category()])).toBeNull();
  });
});
