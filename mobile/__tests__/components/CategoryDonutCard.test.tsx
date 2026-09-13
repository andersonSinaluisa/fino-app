import { render } from '@testing-library/react-native';
import { CategoryDonutCard } from '../../components/home/CategoryDonutCard';
import type { CategoryBreakdownItem } from '../../types/api';

function item(overrides: Partial<CategoryBreakdownItem> = {}): CategoryBreakdownItem {
  return {
    categoryId: 'cat-1',
    name: 'Comida',
    icon: 'utensils',
    color: '#4E9F73',
    total: 100,
    percentage: 100,
    count: 3,
    ...overrides,
  };
}

describe('CategoryDonutCard', () => {
  it('shows a legend row per category with its share of the month', async () => {
    const items = [
      item({ categoryId: 'a', name: 'Comida', total: 60, percentage: 60 }),
      item({ categoryId: 'b', name: 'Transporte', total: 40, percentage: 40 }),
    ];

    const screen = await render(<CategoryDonutCard items={items} totalExpense={100} hidden={false} />);

    expect(screen.getByText('Comida')).toBeTruthy();
    expect(screen.getByText('Transporte')).toBeTruthy();
    expect(screen.getByText('60.0%')).toBeTruthy();
    expect(screen.getByText('40.0%')).toBeTruthy();
  });

  it('groups categories past the top 5 into "Otros" instead of dropping them silently', async () => {
    const items = Array.from({ length: 7 }, (_, index) =>
      item({ categoryId: `cat-${index}`, name: `Categoría ${index}`, total: 7 - index, percentage: 100 / 7 }),
    );

    const screen = await render(<CategoryDonutCard items={items} totalExpense={28} hidden={false} />);

    expect(screen.getByText('Otros')).toBeTruthy();
  });

  it('hides the total in the center of the ring when amounts are hidden', async () => {
    const items = [item({ total: 100, percentage: 100 })];

    const screen = await render(<CategoryDonutCard items={items} totalExpense={100} hidden />);

    expect(screen.getByText('••••')).toBeTruthy();
  });

  it('shows an empty state instead of an empty ring when nothing is categorized yet', async () => {
    const screen = await render(<CategoryDonutCard items={[]} totalExpense={0} hidden={false} />);

    expect(screen.getByText('Todavía no hay gastos categorizados este mes.')).toBeTruthy();
  });
});
