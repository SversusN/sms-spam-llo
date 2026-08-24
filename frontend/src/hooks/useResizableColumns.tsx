import React, { useState, useCallback } from 'react';
import { Resizable } from 'react-resizable';
import type { ColumnsType } from 'antd/es/table';

interface ResizableTitleProps {
  onResize: (e: React.SyntheticEvent, data: { size: { width: number } }) => void;
  width: number;
  [key: string]: any;
}

const ResizableTitle = (props: ResizableTitleProps) => {
  const { onResize, width, ...restProps } = props;

  if (!width) {
    return <th {...restProps} />;
  }

  return (
    <Resizable
      width={width}
      height={0}
      handle={
        <span
          className="react-resizable-handle"
          onClick={(e) => {
            e.stopPropagation();
          }}
        />
      }
      onResize={onResize}
      draggableOpts={{ enableUserSelectHack: false }}
    >
      <th {...restProps} />
    </Resizable>
  );
};

export function useResizableColumns<T extends object>(initialColumns: ColumnsType<T>) {
  const [columns, setColumns] = useState<ColumnsType<T>>(initialColumns);

  const handleResize = useCallback(
    (index: number) =>
      (_e: React.SyntheticEvent, { size }: { size: { width: number } }) => {
        setColumns((prev) => {
          const next = [...prev];
          next[index] = {
            ...next[index],
            width: size.width,
          };
          return next;
        });
      },
    []
  );

  const resizableColumns = columns.map((col, index) => ({
    ...col,
    onHeaderCell: (column: any) => ({
      width: column.width,
      onResize: handleResize(index),
    }),
  }));

  const components = {
    header: {
      cell: ResizableTitle,
    },
  };

  return { columns: resizableColumns, components, setColumns };
}
