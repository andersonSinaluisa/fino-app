import { toQueryString } from '../services/endpoints';

describe('toQueryString', () => {
  it('omits empty filters so the URL stays clean', () => {
    expect(toQueryString({ page: 1, search: undefined, direction: '' })).toBe('?page=1');
  });

  it('returns an empty string when there is nothing to send', () => {
    expect(toQueryString({ search: undefined })).toBe('');
  });

  it('encodes values that would otherwise break the query', () => {
    expect(toQueryString({ search: 'café & té' })).toBe('?search=caf%C3%A9%20%26%20t%C3%A9');
  });
});
