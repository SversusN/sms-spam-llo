import React, { useState, useEffect } from 'react';
import {
  Table,
  Tabs,
  Card,
  Typography,
  Button,
  Space,
  Tag,
  message,
  Row,
  Col,
  Form,
  Input,
  DatePicker,
  Select,
} from 'antd';
import { useNavigate } from 'react-router-dom';
import { getSmsLogs, getSmsQueue, type SmsLog, type SmsQueueItem } from '../api/queue';
import { useAuth } from '../context/AuthContext';
import { useResizableColumns } from '../hooks/useResizableColumns';
import CopyableText from '../components/CopyableText';
import dayjs from 'dayjs';

const { Title } = Typography;
const { RangePicker } = DatePicker;
const { Option } = Select;

const STORAGE_KEY = 'journals_filters';
const DEFAULT_PAGE_SIZE = 20;

const statusOptions = [
  { value: '', label: 'Все' },
  { value: 'Pending', label: 'В очереди' },
  { value: 'Sent', label: 'Отправлено' },
  { value: 'StubSent', label: 'Отправлено (заглушка)' },
  { value: 'Failed', label: 'Ошибка' },
];

interface StoredFilters {
  queue?: Record<string, any>;
  logs?: Record<string, any>;
  pageSize?: number;
}

const loadStoredFilters = (): StoredFilters | null => {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    return raw ? JSON.parse(raw) : null;
  } catch {
    return null;
  }
};

const saveStoredFilters = (queueValues: any, logsValues: any, pageSize: number) => {
  try {
    const stored = loadStoredFilters() || {};
    const serializeValues = (values: any) => {
      const result: Record<string, any> = {};
      if (values.status) result.status = values.status;
      if (values.individualSnils) result.individualSnils = values.individualSnils;
      if (values.recipeId) result.recipeId = values.recipeId;
      if (values.period?.length === 2) {
        result.period = [values.period[0].format('YYYY-MM-DD'), values.period[1].format('YYYY-MM-DD')];
      }
      return result;
    };

    const toStore: StoredFilters = {
      ...stored,
      queue: serializeValues(queueValues),
      logs: serializeValues(logsValues),
      pageSize,
    };
    localStorage.setItem(STORAGE_KEY, JSON.stringify(toStore));
  } catch {
    // ignore
  }
};

const buildInitialValues = (stored: StoredFilters | null, tab: 'queue' | 'logs') => {
  const values = stored?.[tab];
  if (!values) return {};

  return {
    ...values,
    period: values.period?.length === 2
      ? [dayjs(values.period[0]), dayjs(values.period[1])]
      : undefined,
  };
};

const JournalsPage: React.FC = () => {
  const navigate = useNavigate();
  const { userName } = useAuth();
  const [activeTab, setActiveTab] = useState('queue');

  const stored = loadStoredFilters();
  const initialPageSize = stored?.pageSize ?? DEFAULT_PAGE_SIZE;

  // Queue
  const [queueForm] = Form.useForm();
  const [queueData, setQueueData] = useState<SmsQueueItem[]>([]);
  const [queueLoading, setQueueLoading] = useState(false);
  const [queuePagination, setQueuePagination] = useState({ current: 1, pageSize: initialPageSize, total: 0 });

  // Logs
  const [logsForm] = Form.useForm();
  const [logsData, setLogsData] = useState<SmsLog[]>([]);
  const [logsLoading, setLogsLoading] = useState(false);
  const [logsPagination, setLogsPagination] = useState({ current: 1, pageSize: initialPageSize, total: 0 });

  useEffect(() => {
    const queueInitial = buildInitialValues(stored, 'queue');
    const logsInitial = buildInitialValues(stored, 'logs');
    queueForm.setFieldsValue(queueInitial);
    logsForm.setFieldsValue(logsInitial);
    fetchQueue(1, initialPageSize, queueInitial);
    fetchLogs(1, initialPageSize, logsInitial);
  }, []);

  const queueInitialColumns: import('antd/es/table').ColumnsType<SmsQueueItem> = [
    { title: 'ID', dataIndex: 'id', width: 80, render: (v: number) => <CopyableText value={v} /> },
    { title: 'ID рецепта', dataIndex: 'recipeId', width: 100, render: (v: number) => <CopyableText value={v} /> },
    { title: 'СНИЛС', dataIndex: 'individualSnils', width: 140, render: (v: string | null) => <CopyableText value={v} /> },
    { title: 'Статус', dataIndex: 'status', width: 120, render: (v: string) => (
      <Tag color={v === 'Pending' ? 'gold' : v === 'Sent' || v === 'StubSent' ? 'green' : 'red'}>{v}</Tag>
    )},
    { title: 'Ошибка', dataIndex: 'errorMessage', width: 200, render: (v: string | null) => <CopyableText value={v} /> },
    { title: 'Создано', dataIndex: 'createdAt', width: 160, render: (v: string) => dayjs(v).format('DD.MM.YYYY HH:mm') },
    { title: 'Обработано', dataIndex: 'processedAt', width: 160, render: (v: string | null) => v ? dayjs(v).format('DD.MM.YYYY HH:mm') : '-' },
  ];

  const logsInitialColumns: import('antd/es/table').ColumnsType<SmsLog> = [
    { title: 'ID', dataIndex: 'id', width: 80, render: (v: number) => <CopyableText value={v} /> },
    { title: 'ID рецепта', dataIndex: 'recipeId', width: 100, render: (v: number) => <CopyableText value={v} /> },
    { title: 'СНИЛС', dataIndex: 'individualSnils', width: 140, render: (v: string | null) => <CopyableText value={v} /> },
    { title: 'Телефон', dataIndex: 'phone', width: 140, render: (v: string) => <CopyableText value={v} /> },
    { title: 'Сообщение', dataIndex: 'message', width: 250, render: (v: string) => <CopyableText value={v} /> },
    { title: 'Статус', dataIndex: 'status', width: 120, render: (v: string) => (
      <Tag color={v === 'Sent' || v === 'StubSent' ? 'green' : 'red'}>{v}</Tag>
    )},
    { title: 'Статус доставки', dataIndex: 'deliveryStatus', width: 140, render: (v: string | null) => {
      if (!v) return '-';
      const color = v === 'delivered' ? 'green' : v === 'undeliverable' || v === 'rejected' || v === 'expired' ? 'red' : 'gold';
      return <Tag color={color}>{v}</Tag>;
    }},
    { title: 'Ответ шлюза', dataIndex: 'providerResponse', width: 250, render: (v: string | null) => <CopyableText value={v} /> },
    { title: 'Дата', dataIndex: 'createdAt', width: 160, render: (v: string) => dayjs(v).format('DD.MM.YYYY HH:mm') },
  ];

  const { columns: queueColumns, components: queueComponents } = useResizableColumns(queueInitialColumns);
  const { columns: logsColumns, components: logsComponents } = useResizableColumns(logsInitialColumns);

  const fetchQueue = async (page = 1, pageSize = queuePagination.pageSize, formValues?: any) => {
    setQueueLoading(true);
    try {
      const values = formValues || queueForm.getFieldsValue();
      const filter: Record<string, any> = {};
      if (values.status) filter.status = values.status;
      if (values.individualSnils) filter.individualSnils = values.individualSnils.replace(/\D/g, '');
      if (values.recipeId) filter.recipeId = Number(values.recipeId);
      if (values.period && values.period.length === 2) {
        filter.dateFrom = values.period[0].format('YYYY-MM-DD');
        filter.dateTo = values.period[1].format('YYYY-MM-DD');
      }

      const items = await getSmsQueue({ page, pageSize, ...filter });
      setQueueData(items);
      setQueuePagination({ current: page, pageSize, total: items.length });
    } catch (error: any) {
      message.error(error.response?.data?.message || 'Ошибка загрузки очереди');
    } finally {
      setQueueLoading(false);
    }
  };

  const fetchLogs = async (page = 1, pageSize = logsPagination.pageSize, formValues?: any) => {
    setLogsLoading(true);
    try {
      const values = formValues || logsForm.getFieldsValue();
      const filter: Record<string, any> = {};
      if (values.status) filter.status = values.status;
      if (values.individualSnils) filter.individualSnils = values.individualSnils.replace(/\D/g, '');
      if (values.recipeId) filter.recipeId = Number(values.recipeId);
      if (values.period && values.period.length === 2) {
        filter.dateFrom = values.period[0].format('YYYY-MM-DD');
        filter.dateTo = values.period[1].format('YYYY-MM-DD');
      }

      const items = await getSmsLogs({ page, pageSize, ...filter });
      setLogsData(items);
      setLogsPagination({ current: page, pageSize, total: items.length });
    } catch (error: any) {
      message.error(error.response?.data?.message || 'Ошибка загрузки лога');
    } finally {
      setLogsLoading(false);
    }
  };

  const handleQueueSubmit = () => {
    saveStoredFilters(queueForm.getFieldsValue(), logsForm.getFieldsValue(), queuePagination.pageSize);
    fetchQueue(1);
  };

  const handleLogsSubmit = () => {
    saveStoredFilters(queueForm.getFieldsValue(), logsForm.getFieldsValue(), logsPagination.pageSize);
    fetchLogs(1);
  };

  const handleQueueTableChange = (p: any) => {
    setQueuePagination((prev) => ({ ...prev, pageSize: p.pageSize }));
    saveStoredFilters(queueForm.getFieldsValue(), logsForm.getFieldsValue(), p.pageSize);
    fetchQueue(p.current, p.pageSize);
  };

  const handleLogsTableChange = (p: any) => {
    setLogsPagination((prev) => ({ ...prev, pageSize: p.pageSize }));
    saveStoredFilters(queueForm.getFieldsValue(), logsForm.getFieldsValue(), p.pageSize);
    fetchLogs(p.current, p.pageSize);
  };

  const handleReset = (formInstance: any, tab: 'queue' | 'logs') => {
    localStorage.removeItem(STORAGE_KEY);
    formInstance.resetFields();
    if (tab === 'queue') {
      setQueuePagination((prev) => ({ ...prev, pageSize: DEFAULT_PAGE_SIZE }));
      fetchQueue(1, DEFAULT_PAGE_SIZE, {});
    } else {
      setLogsPagination((prev) => ({ ...prev, pageSize: DEFAULT_PAGE_SIZE }));
      fetchLogs(1, DEFAULT_PAGE_SIZE, {});
    }
  };

  const renderFilters = (formInstance: any, onSubmit: () => void, tab: 'queue' | 'logs') => (
    <Form form={formInstance} layout="vertical" onFinish={onSubmit}>
      <Row gutter={16}>
        <Col xs={24} md={6} lg={5}>
          <Form.Item name="status" label="Статус">
            <Select allowClear placeholder="Все">
              {statusOptions.map(o => (
                <Option key={o.value} value={o.value}>{o.label}</Option>
              ))}
            </Select>
          </Form.Item>
        </Col>
        <Col xs={24} md={6} lg={5}>
          <Form.Item name="individualSnils" label="СНИЛС">
            <Input placeholder="000-000-000 00" allowClear />
          </Form.Item>
        </Col>
        <Col xs={24} md={6} lg={5}>
          <Form.Item name="recipeId" label="ID рецепта">
            <Input type="number" placeholder="Номер рецепта" allowClear />
          </Form.Item>
        </Col>
        <Col xs={24} md={6} lg={5}>
          <Form.Item name="period" label="Период">
            <RangePicker style={{ width: '100%' }} format="DD.MM.YYYY" />
          </Form.Item>
        </Col>
      </Row>
      <Row>
        <Col>
          <Space>
            <Button type="primary" htmlType="submit">Применить</Button>
            <Button onClick={() => handleReset(formInstance, tab)}>Сбросить</Button>
          </Space>
        </Col>
      </Row>
    </Form>
  );

  return (
    <div style={{ padding: 24 }}>
      <Row justify="space-between" align="middle" style={{ marginBottom: 16 }}>
        <Col>
          <Title level={3} style={{ margin: 0 }}>Журналы SMS</Title>
        </Col>
        <Col>
          <Space>
            <span>{userName}</span>
            <Button onClick={() => navigate('/recipes')}>К рецептам</Button>
            <Button onClick={() => navigate('/consents')}>Согласия</Button>
          </Space>
        </Col>
      </Row>

      <Card>
        <Tabs activeKey={activeTab} onChange={setActiveTab}>
          <Tabs.TabPane tab="Очередь отправки" key="queue">
            <Card size="small" style={{ marginBottom: 16 }}>
              {renderFilters(queueForm, handleQueueSubmit, 'queue')}
            </Card>
            <Table
              rowKey="id"
              columns={queueColumns}
              components={queueComponents}
              dataSource={queueData}
              className="table-cell-wrap"
              loading={queueLoading}
              pagination={{
                ...queuePagination,
                showSizeChanger: true,
                showTotal: (total) => `Всего: ${total}`,
              }}
              onChange={handleQueueTableChange}
              scroll={{ x: 'max-content' }}
              size="small"
            />
          </Tabs.TabPane>
          <Tabs.TabPane tab="Журнал отправки" key="logs">
            <Card size="small" style={{ marginBottom: 16 }}>
              {renderFilters(logsForm, handleLogsSubmit, 'logs')}
            </Card>
            <Table
              rowKey="id"
              columns={logsColumns}
              components={logsComponents}
              dataSource={logsData}
              className="table-cell-wrap"
              loading={logsLoading}
              pagination={{
                ...logsPagination,
                showSizeChanger: true,
                showTotal: (total) => `Всего: ${total}`,
              }}
              onChange={handleLogsTableChange}
              scroll={{ x: 'max-content' }}
              size="small"
            />
          </Tabs.TabPane>
        </Tabs>
      </Card>
    </div>
  );
};

export default JournalsPage;
