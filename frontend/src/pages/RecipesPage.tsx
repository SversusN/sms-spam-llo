import React, { useState, useEffect, useCallback } from 'react';
import {
  Table,
  Form,
  Input,
  DatePicker,
  Button,
  Space,
  Card,
  message,
  Typography,
  Row,
  Col,
  Badge,
  Checkbox,
  Tag,
  Select,
  Spin,
} from 'antd';
import { useNavigate } from 'react-router-dom';
import { getRecipes, exportRecipes, getPharmacies, type RecipeDto, type RecipeFilterRequest, type PharmacyDto } from '../api/recipes';
import { enqueueRecipes } from '../api/queue';
import { getFeatures } from '../api/settings';
import { useAuth } from '../context/AuthContext';
import { useResizableColumns } from '../hooks/useResizableColumns';
import CopyableText from '../components/CopyableText';
import dayjs from 'dayjs';

const { Title } = Typography;
const { RangePicker } = DatePicker;

const STORAGE_KEY = 'recipes_filters';
const DEFAULT_PAGE_SIZE = 20;

const formatSnils = (value: string = ''): string => {
  const digits = value.replace(/\D/g, '').slice(0, 11);
  if (digits.length <= 3) return digits;
  if (digits.length <= 6) return `${digits.slice(0, 3)}-${digits.slice(3)}`;
  if (digits.length <= 9) return `${digits.slice(0, 3)}-${digits.slice(3, 6)}-${digits.slice(6)}`;
  return `${digits.slice(0, 3)}-${digits.slice(3, 6)}-${digits.slice(6, 9)} ${digits.slice(9)}`;
};

interface StoredFilters {
  patientName?: string;
  patientPhone?: string;
  lsName?: string;
  individualSnils?: string;
  contractorGuid?: string;
  onlyDeferred?: boolean;
  onlyNotSent?: boolean;
  pageSize?: number;
}

const defaultDateFrom = dayjs().subtract(1, 'month');
const defaultDateTo = dayjs();

const loadStoredFilters = (): StoredFilters | null => {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    return raw ? JSON.parse(raw) : null;
  } catch {
    return null;
  }
};

const saveStoredFilters = (values: any, pageSize: number) => {
  try {
    const toStore: StoredFilters = {
      patientName: values.patientName || undefined,
      patientPhone: values.patientPhone || undefined,
      lsName: values.lsName || undefined,
      individualSnils: values.individualSnils || undefined,
      contractorGuid: values.contractorGuid || undefined,
      onlyDeferred: values.onlyDeferred,
      onlyNotSent: values.onlyNotSent,
      pageSize,
    };
    localStorage.setItem(STORAGE_KEY, JSON.stringify(toStore));
  } catch {
    // ignore
  }
};

const buildInitialValues = (stored: StoredFilters | null) => {
  if (!stored) {
    return {
      period: [defaultDateFrom, defaultDateTo],
      onlyDeferred: true,
      onlyNotSent: true,
    };
  }

  return {
    // Период не сохраняем — всегда подставляем значения по умолчанию
    period: [defaultDateFrom, defaultDateTo],
    patientName: stored.patientName,
    patientPhone: stored.patientPhone,
    lsName: stored.lsName,
    individualSnils: stored.individualSnils,
    onlyDeferred: stored.onlyDeferred ?? true,
    onlyNotSent: stored.onlyNotSent ?? true,
  };
};

const RecipesPage: React.FC = () => {
  const navigate = useNavigate();
  const { userName, logout } = useAuth();
  const [form] = Form.useForm();
  const [data, setData] = useState<RecipeDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [selectedRowKeys, setSelectedRowKeys] = useState<React.Key[]>([]);
  const [requireMailingConsent, setRequireMailingConsent] = useState(true);

  const stored = loadStoredFilters();
  const initialPageSize = stored?.pageSize ?? DEFAULT_PAGE_SIZE;

  const [pagination, setPagination] = useState({
    current: 1,
    pageSize: initialPageSize,
    total: 0,
  });
  const [pharmacies, setPharmacies] = useState<PharmacyDto[]>([]);

  const [pharmaciesLoading, setPharmaciesLoading] = useState(true);

  useEffect(() => {
    setPharmaciesLoading(true);
    getPharmacies()
      .then((data) => {
        setPharmacies(data);
        if (stored?.contractorGuid && data.some((p) => p.guid === stored.contractorGuid)) {
          form.setFieldsValue({ contractorGuid: stored.contractorGuid });
        }
      })
      .catch(() => message.error('Ошибка загрузки аптек'))
      .finally(() => {
        setPharmaciesLoading(false);
        fetchData(1, initialPageSize);
      });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    getFeatures()
      .then((features) => setRequireMailingConsent(features.requireMailingConsent))
      .catch(() => setRequireMailingConsent(true));
  }, []);

  useEffect(() => {
    const initialValues = buildInitialValues(stored);
    form.setFieldsValue(initialValues);
  }, []);

  const baseColumns: import('antd/es/table').ColumnsType<RecipeDto> = [
    { title: 'ID', dataIndex: 'recipeId', width: 80, render: (v: number) => <CopyableText value={v} /> },
    { title: 'Аптека', dataIndex: 'apName', width: 180, render: (v: string) => <CopyableText value={v} /> },
    { title: 'Лекарство', dataIndex: 'lsName', width: 200, render: (v: string) => <CopyableText value={v} /> },
    { title: 'Дата поступления', dataIndex: 'incomeDate', width: 140, render: (v: string | null) => v ? dayjs(v).format('DD.MM.YYYY') : '-' },
    { title: 'Дата/номер рецепта', dataIndex: 'dateNumberRecipe', width: 160, render: (v: string | null) => <CopyableText value={v} /> },
    { title: 'Срок действия до', dataIndex: 'dateIssueEnd', width: 130, render: (v: string | null) => v ? dayjs(v).format('DD.MM.YYYY') : '-' },
    { title: 'Пациент', dataIndex: 'patientName', width: 160, render: (v: string | null) => <CopyableText value={v} /> },
    { title: 'Телефон', dataIndex: 'patientPhone', width: 130, render: (v: string | null) => <CopyableText value={v} /> },
    { title: 'СНИЛС', dataIndex: 'individualSnils', width: 130, render: (v: string | null) => <CopyableText value={v} /> },
    ...(requireMailingConsent ? [{
      title: 'Рассылка',
      dataIndex: 'hasMailingConsent',
      width: 110,
      render: (v: boolean) => v ? <Tag color="green">Есть согласие</Tag> : <Tag color="red">Нет согласия</Tag>,
    }] : []),
    { title: 'Программа', dataIndex: 'program', width: 90, render: (v: string | null) => <CopyableText value={v} /> },
    { title: 'Дозировка', dataIndex: 'dosage', width: 120, render: (v: string | null) => <CopyableText value={v} /> },
    { title: 'Кол-во', dataIndex: 'quantity', width: 100, render: (v: string | null) => <CopyableText value={v} /> },
    {
      title: 'Статус SMS',
      dataIndex: 'smsStatus',
      width: 120,
      render: (v: string | null) => {
        if (!v) return <Tag>Не отправлено</Tag>;
        if (v === 'Sent' || v === 'StubSent') return <Tag color="green">Отправлено</Tag>;
        if (v === 'Failed') return <Tag color="red">Ошибка</Tag>;
        if (v === 'Pending') return <Tag color="gold">В очереди</Tag>;
        return <Tag>{v}</Tag>;
      },
    },
    {
      title: 'Доставка',
      dataIndex: 'smsDeliveryStatus',
      width: 120,
      render: (v: string | null) => {
        if (!v) return '-';
        if (v === 'delivered') return <Tag color="green">Доставлено</Tag>;
        if (v === 'undeliverable' || v === 'rejected' || v === 'expired') return <Tag color="red">{v}</Tag>;
        return <Tag color="gold">{v}</Tag>;
      },
    },
    { title: 'Дата продажи', dataIndex: 'saleDate', width: 130, render: (v: string | null) => v ? dayjs(v).format('DD.MM.YYYY') : '-' },
    { title: 'SMS дата', dataIndex: 'smsDate', width: 120, render: (v: string | null) => v ? dayjs(v).format('DD.MM.YYYY HH:mm') : '-' },
  ];

  const { columns, components } = useResizableColumns(baseColumns);

  const buildFilter = (values: any, page: number, pageSize: number): RecipeFilterRequest => {
    const filter: RecipeFilterRequest = {
      page,
      pageSize,
      patientName: values.patientName || undefined,
      patientPhone: values.patientPhone || undefined,
      lsName: values.lsName || undefined,
      individualSnils: values.individualSnils || undefined,
      contractorGuid: values.contractorGuid || undefined,
      onlyDeferred: values.onlyDeferred || false,
      onlyNotSent: values.onlyNotSent !== false,
      sortColumn: 'IncomeDate',
      sortDirection: 'DESC',
    };

    if (values.period && values.period.length === 2) {
      filter.dateFrom = values.period[0].format('YYYY-MM-DD');
      filter.dateTo = values.period[1].format('YYYY-MM-DD');
    }

    return filter;
  };

  const fetchData = useCallback(async (page = 1, pageSize = pagination.pageSize) => {
    setLoading(true);
    try {
      const values = form.getFieldsValue();
      const filter = buildFilter(values, page, pageSize);
      const result = await getRecipes(filter);
      setData(result.items);
      setPagination({
        current: result.page,
        pageSize: result.pageSize,
        total: result.totalCount,
      });
    } catch (error: any) {
      message.error(error.response?.data?.message || 'Ошибка загрузки данных');
    } finally {
      setLoading(false);
    }
  }, [form, pagination.pageSize]);

  const handleTableChange = (newPagination: any) => {
    setPagination((prev) => ({ ...prev, pageSize: newPagination.pageSize }));
    const values = form.getFieldsValue();
    saveStoredFilters(values, newPagination.pageSize);
    fetchData(newPagination.current, newPagination.pageSize);
  };

  const handleApply = () => {
    const values = form.getFieldsValue();
    saveStoredFilters(values, pagination.pageSize);
    fetchData(1);
  };

  const handleExport = async () => {
    setLoading(true);
    try {
      const values = form.getFieldsValue();
      const filter = buildFilter(values, 1, 1000);
      const blob = await exportRecipes(filter);
      const url = window.URL.createObjectURL(new Blob([blob]));
      const link = document.createElement('a');
      link.href = url;
      link.setAttribute('download', `Рецепты_${dayjs().format('YYYYMMDD_HHmmss')}.xlsx`);
      document.body.appendChild(link);
      link.click();
      link.remove();
      window.URL.revokeObjectURL(url);
      message.success('Выгрузка завершена');
    } catch (error: any) {
      message.error(error.response?.data?.message || 'Ошибка выгрузки');
    } finally {
      setLoading(false);
    }
  };

  const handleSend = async () => {
    if (selectedRowKeys.length === 0) {
      message.warning('Выберите хотя бы один рецепт');
      return;
    }
    try {
      const result = await enqueueRecipes({ recipeIds: selectedRowKeys.map(Number) });
      message.success(`В очередь добавлено: ${result.enqueued} рецептов`);
      setSelectedRowKeys([]);
      fetchData(pagination.current, pagination.pageSize);
    } catch (error: any) {
      message.error(error.response?.data?.message || 'Ошибка постановки в очередь');
    }
  };

  const canSelectRecipe = (record: RecipeDto) => {
    const consentOk = !requireMailingConsent || record.hasMailingConsent;
    return consentOk
      && record.smsStatus !== 'Sent'
      && record.smsStatus !== 'StubSent'
      && record.smsStatus !== 'Pending';
  };

  const rowSelection = {
    selectedRowKeys,
    onChange: (newSelectedRowKeys: React.Key[]) => {
      setSelectedRowKeys(newSelectedRowKeys);
    },
    getCheckboxProps: (record: RecipeDto) => ({
      disabled: !canSelectRecipe(record),
    }),
  };

  const handleReset = () => {
    localStorage.removeItem(STORAGE_KEY);
    form.resetFields();
    form.setFieldsValue({
      period: [defaultDateFrom, defaultDateTo],
      onlyDeferred: true,
      onlyNotSent: true,
    });
    setPagination((prev) => ({ ...prev, pageSize: DEFAULT_PAGE_SIZE }));
    fetchData(1, DEFAULT_PAGE_SIZE);
  };

  return (
    <div style={{ padding: 24 }}>
      <Row justify="space-between" align="middle" style={{ marginBottom: 16 }}>
        <Col>
          <Title level={3} style={{ margin: 0 }}>Журнал рецептов</Title>
        </Col>
        <Col>
          <Space>
            <span>{userName}</span>
            <Button onClick={() => navigate('/journals')}>Журналы SMS</Button>
            <Button onClick={() => navigate('/consents')}>Согласия</Button>
            <Button onClick={logout}>Выйти</Button>
          </Space>
        </Col>
      </Row>

      <Card style={{ marginBottom: 16 }}>
        <Form
          form={form}
          layout="vertical"
          onFinish={handleApply}
          initialValues={buildInitialValues(stored)}
        >
          <Row gutter={16}>
            <Col xs={24} md={8} lg={4}>
              <Form.Item name="period" label="Период поступления">
                <RangePicker style={{ width: '100%' }} format="DD.MM.YYYY" />
              </Form.Item>
            </Col>
            <Col xs={24} md={8} lg={4}>
              <Form.Item name="patientName" label="Пациент">
                <Input placeholder="ФИО" allowClear />
              </Form.Item>
            </Col>
            <Col xs={24} md={8} lg={4}>
              <Form.Item name="patientPhone" label="Телефон">
                <Input placeholder="Телефон" allowClear />
              </Form.Item>
            </Col>
            <Col xs={24} md={8} lg={4}>
              <Form.Item name="lsName" label="Лекарственное средство">
                <Input placeholder="Название ЛС" allowClear />
              </Form.Item>
            </Col>
            {(pharmaciesLoading || pharmacies.length > 1) && (
              <Col xs={24} md={8} lg={4}>
                <Form.Item name="contractorGuid" label="Аптека">
                  {pharmaciesLoading ? (
                    <Select
                      disabled
                      placeholder="Загрузка аптек..."
                      suffixIcon={<Spin size="small" />}
                    />
                  ) : (
                    <Select
                      allowClear
                      showSearch
                      placeholder="Выберите аптеку"
                      optionFilterProp="label"
                      filterOption={(input: string, option: any) =>
                        (option?.label as string)?.toLowerCase().includes(input.toLowerCase())
                      }
                      onChange={() => {
                        const values = form.getFieldsValue();
                        saveStoredFilters(values, pagination.pageSize);
                        fetchData(1);
                      }}
                    >
                      {pharmacies.map((p) => (
                        <Select.Option key={p.guid} value={p.guid} label={p.name}>{p.name}</Select.Option>
                      ))}
                    </Select>
                  )}
                </Form.Item>
              </Col>
            )}
          </Row>
          <Row gutter={16} align="bottom">
            <Col xs={24} md={8} lg={6}>
              <Form.Item name="individualSnils" label="СНИЛС">
                <Input
                  placeholder="000-000-000 00"
                  allowClear
                  maxLength={14}
                  onChange={(e) => {
                    const formatted = formatSnils(e.target.value);
                    form.setFieldsValue({ individualSnils: formatted });
                  }}
                />
              </Form.Item>
            </Col>
            <Col xs={24} md={8} lg={4}>
              <Form.Item name="onlyDeferred" valuePropName="checked" style={{ marginBottom: 24 }}>
                <Checkbox>Только отсроченные</Checkbox>
              </Form.Item>
            </Col>
            <Col xs={24} md={8} lg={4}>
              <Form.Item name="onlyNotSent" valuePropName="checked" style={{ marginBottom: 24 }}>
                <Checkbox>Только не отправленные</Checkbox>
              </Form.Item>
            </Col>
            <Col xs={24} md={24} lg={10}>
              <Space style={{ marginBottom: 24 }}>
                <Button type="primary" htmlType="submit">
                  Применить фильтры
                </Button>
                <Button onClick={handleReset}>
                  Сбросить
                </Button>
                <Button onClick={handleExport} loading={loading}>
                  Выгрузить в Excel
                </Button>
              </Space>
            </Col>
          </Row>
        </Form>
      </Card>

      <Row style={{ marginBottom: 16 }}>
        <Col>
          <Badge count={selectedRowKeys.length} showZero color="#108ee9">
            <Button type="primary" onClick={handleSend} disabled={selectedRowKeys.length === 0}>
              Отправить SMS
            </Button>
          </Badge>
        </Col>
      </Row>

      <Table
        rowKey="recipeId"
        rowSelection={rowSelection}
        columns={columns}
        components={components}
        dataSource={data}
        className="table-cell-wrap"
        loading={{ spinning: loading, indicator: <img src="/loading.gif" alt="Загрузка" style={{ width: 180, height: 'auto' }} /> }}
        pagination={{
          ...pagination,
          showSizeChanger: true,
          showTotal: (total) => `Всего: ${total}`,
        }}
        onChange={handleTableChange}
        scroll={{ x: 'max-content' }}
        size="small"
        rowClassName={(record) => canSelectRecipe(record) ? '' : 'ant-table-row-disabled'}
        onRow={(record) => ({
          onClick: () => {
            if (!canSelectRecipe(record)) return;
            const key = record.recipeId;
            if (selectedRowKeys.includes(key)) {
              setSelectedRowKeys(selectedRowKeys.filter((k) => k !== key));
            } else {
              setSelectedRowKeys([...selectedRowKeys, key]);
            }
          },
          style: { cursor: canSelectRecipe(record) ? 'pointer' : 'not-allowed' },
        })}
      />
    </div>
  );
};

export default RecipesPage;
