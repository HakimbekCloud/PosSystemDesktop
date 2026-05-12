import { memo } from 'react';
import { Search, X } from 'lucide-react';
import { Input, type InputProps } from './Input';
import { Button } from './Button';

export type SearchBarProps = InputProps & {
  onClear?: () => void;
};

function SearchBarBase({ value, onClear, ...props }: SearchBarProps) {
  return (
    <Input
      value={value}
      leftIcon={<Search />}
      rightSlot={
        value && onClear ? (
          <Button aria-label="Clear search" variant="ghost" size="icon" className="size-8 rounded-lg" onClick={onClear}>
            <X className="size-4" />
          </Button>
        ) : null
      }
      {...props}
    />
  );
}

export const SearchBar = memo(SearchBarBase);
