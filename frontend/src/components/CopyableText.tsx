import React, { useState } from 'react';
import { CopyOutlined, CheckOutlined } from '@ant-design/icons';
import { message } from 'antd';

interface CopyableTextProps {
  value: string | number | null | undefined;
  className?: string;
}

const CopyableText: React.FC<CopyableTextProps> = ({ value, className }) => {
  const [hovered, setHovered] = useState(false);
  const [copied, setCopied] = useState(false);

  const text = value === null || value === undefined ? '' : String(value);
  if (!text) return <span className={className}>-</span>;

  const handleCopy = async (e: React.MouseEvent) => {
    e.stopPropagation();
    try {
      await navigator.clipboard.writeText(text);
      setCopied(true);
      message.success('Скопировано');
      setTimeout(() => setCopied(false), 1500);
    } catch {
      message.error('Не удалось скопировать');
    }
  };

  return (
    <div
      className={`copyable-text ${className || ''}`}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
    >
      <span className="copyable-text-value">{text}</span>
      {hovered && (
        <span className="copyable-text-icon" onClick={handleCopy}>
          {copied ? <CheckOutlined style={{ color: '#52c41a' }} /> : <CopyOutlined />}
        </span>
      )}
    </div>
  );
};

export default CopyableText;
