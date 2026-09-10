import React, { useState } from 'react';
import { CopyOutlined, CheckOutlined } from '@ant-design/icons';
import { message } from 'antd';

interface CopyableTextProps {
  value: string | number | null | undefined;
  className?: string;
}

const fallbackCopy = (text: string): boolean => {
  const textarea = document.createElement('textarea');
  textarea.value = text;
  textarea.style.position = 'fixed';
  textarea.style.opacity = '0';
  document.body.appendChild(textarea);
  textarea.focus();
  textarea.select();
  let ok = false;
  try {
    ok = document.execCommand('copy');
  } catch {
    ok = false;
  }
  document.body.removeChild(textarea);
  return ok;
};

const copyToClipboard = async (text: string): Promise<void> => {
  // Clipboard API работает только в Secure Context (https или localhost),
  // поэтому на http://<ip> используем fallback через execCommand
  try {
    if (navigator.clipboard && window.isSecureContext) {
      await navigator.clipboard.writeText(text);
      return;
    }
  } catch (err) {
    console.warn('Clipboard API failed, using fallback:', err);
  }
  if (!fallbackCopy(text)) {
    throw new Error('Clipboard is unavailable');
  }
};

const CopyableText: React.FC<CopyableTextProps> = ({ value, className }) => {
  const [hovered, setHovered] = useState(false);
  const [copied, setCopied] = useState(false);

  const text = value === null || value === undefined ? '' : String(value);
  if (!text) return <span className={className}>-</span>;

  const handleCopy = async (e: React.MouseEvent) => {
    e.stopPropagation();
    try {
      await copyToClipboard(text);
      setCopied(true);
      message.success('Скопировано');
      setTimeout(() => setCopied(false), 1500);
    } catch (err) {
      console.error('Copy failed:', err);
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
