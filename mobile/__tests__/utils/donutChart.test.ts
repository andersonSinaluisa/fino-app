import { buildCategorySlices, buildDonutSegments, MAX_DONUT_SLICES, OTHER_SLICE_ID } from '../../utils/donutChart';

const OTHER_COLOR = '#999999';

function item(overrides: Partial<{ categoryId: string; color: string; percentage: number; total: number }> = {}) {
  return {
    categoryId: 'cat-1',
    color: '#111111',
    percentage: 10,
    total: 10,
    ...overrides,
  };
}

describe('buildCategorySlices', () => {
  it('keeps every category as its own slice when there are 6 or fewer', () => {
    const items = [
      item({ categoryId: 'a', total: 50, percentage: 50 }),
      item({ categoryId: 'b', total: 30, percentage: 30 }),
      item({ categoryId: 'c', total: 20, percentage: 20 }),
    ];

    const slices = buildCategorySlices(items, OTHER_COLOR);

    expect(slices).toEqual([
      { id: 'a', color: '#111111', percentage: 50 },
      { id: 'b', color: '#111111', percentage: 30 },
      { id: 'c', color: '#111111', percentage: 20 },
    ]);
  });

  it('sorts by total descending even if the input is not already sorted', () => {
    const items = [
      item({ categoryId: 'small', total: 5, percentage: 5 }),
      item({ categoryId: 'big', total: 95, percentage: 95 }),
    ];

    const slices = buildCategorySlices(items, OTHER_COLOR);

    expect(slices.map((slice) => slice.id)).toEqual(['big', 'small']);
  });

  it('groups everything past the top 5 into a single "Otros" slice instead of hiding it', () => {
    const items = Array.from({ length: 8 }, (_, index) =>
      item({ categoryId: `cat-${index}`, total: 8 - index, percentage: 100 / 8 }),
    );

    const slices = buildCategorySlices(items, OTHER_COLOR);

    expect(slices).toHaveLength(MAX_DONUT_SLICES);
    expect(slices.slice(0, 5).map((slice) => slice.id)).toEqual(['cat-0', 'cat-1', 'cat-2', 'cat-3', 'cat-4']);

    const other = slices[5]!;
    expect(other.id).toBe(OTHER_SLICE_ID);
    expect(other.color).toBe(OTHER_COLOR);
    // The 3 remaining categories (cat-5, cat-6, cat-7) each carry 100/8 = 12.5%.
    expect(other.percentage).toBeCloseTo(37.5, 5);
  });
});

describe('buildDonutSegments', () => {
  it('converts percentages into cumulative clockwise angles', () => {
    const segments = buildDonutSegments([
      { id: 'a', color: 'red', percentage: 50 },
      { id: 'b', color: 'blue', percentage: 30 },
      { id: 'c', color: 'green', percentage: 20 },
    ]);

    expect(segments.map((segment) => segment.cumulativeAngle)).toEqual([180, 288, 360]);
  });

  it('drops zero-percentage slices so they never paint an invisible wedge', () => {
    const segments = buildDonutSegments([
      { id: 'a', color: 'red', percentage: 100 },
      { id: 'b', color: 'blue', percentage: 0 },
    ]);

    expect(segments).toHaveLength(1);
    expect(segments[0]!.id).toBe('a');
  });

  it('caps the final angle at 360° even if floating point rounding pushes the sum past 100', () => {
    const segments = buildDonutSegments([
      { id: 'a', color: 'red', percentage: 33.34 },
      { id: 'b', color: 'blue', percentage: 33.33 },
      { id: 'c', color: 'green', percentage: 33.34 },
    ]);

    expect(segments[segments.length - 1]!.cumulativeAngle).toBeLessThanOrEqual(360);
  });

  it('returns an empty ring for an empty slice list', () => {
    expect(buildDonutSegments([])).toEqual([]);
  });
});
