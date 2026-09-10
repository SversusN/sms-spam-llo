import React, { useState, useEffect, useCallback } from 'react';
import {
  Table,
  Form,
  Input,
  Button,
  Space,
  Card,
  message,
  Typography,
  Row,
  Col,
  Modal,
  Checkbox,
  DatePicker,
  Tag,
  Select,
} from 'antd';
import { PlusOutlined, SearchOutlined, PrinterOutlined } from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import dayjs from 'dayjs';
import { useAuth } from '../context/AuthContext';
import { useResizableColumns } from '../hooks/useResizableColumns';
import CopyableText from '../components/CopyableText';
import {
  getConsents,
  createConsent,
  lookupPatient,
  revokeConsent,
  type ConsentDto,
  type ConsentFilterRequest,
  type CreateConsentRequest,
} from '../api/consents';

const { Title } = Typography;
const { RangePicker } = DatePicker;

const STORAGE_KEY = 'consents_filters';
const DEFAULT_PAGE_SIZE = 20;

const formatSnils = (value: string = ''): string => {
  const digits = value.replace(/\D/g, '').slice(0, 11);
  if (digits.length <= 3) return digits;
  if (digits.length <= 6) return `${digits.slice(0, 3)}-${digits.slice(3)}`;
  if (digits.length <= 9) return `${digits.slice(0, 3)}-${digits.slice(3, 6)}-${digits.slice(6)}`;
  return `${digits.slice(0, 3)}-${digits.slice(3, 6)}-${digits.slice(6, 9)} ${digits.slice(9)}`;
};

interface StoredFilters {
  patientSnils?: string;
  patientName?: string;
  isConsentGiven?: boolean;
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

const saveStoredFilters = (values: any, pageSize: number) => {
  try {
    const toStore: StoredFilters = {
      patientSnils: values.patientSnils || undefined,
      patientName: values.patientName || undefined,
      isConsentGiven: values.isConsentGiven,
      pageSize,
    };
    localStorage.setItem(STORAGE_KEY, JSON.stringify(toStore));
  } catch {
    // ignore
  }
};

const buildInitialValues = (stored: StoredFilters | null) => {
  if (!stored) return {};
  return {
    patientSnils: stored.patientSnils,
    patientName: stored.patientName,
    isConsentGiven: stored.isConsentGiven,
  };
};

const ConsentsPage: React.FC = () => {
  const navigate = useNavigate();
  const { userName, logout } = useAuth();
  const [form] = Form.useForm();
  const [modalForm] = Form.useForm();
  const [data, setData] = useState<ConsentDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [modalOpen, setModalOpen] = useState(false);
  const [lookupLoading, setLookupLoading] = useState(false);
  const [foundPatient, setFoundPatient] = useState<{ name: string; birthDate: string | null; phone: string | null } | null>(null);

  const stored = loadStoredFilters();
  const initialPageSize = stored?.pageSize ?? DEFAULT_PAGE_SIZE;

  const [pagination, setPagination] = useState({ current: 1, pageSize: initialPageSize, total: 0 });

  useEffect(() => {
    form.setFieldsValue(buildInitialValues(stored));
    fetchData(1, initialPageSize);
  }, []);

  const initialColumns: import('antd/es/table').ColumnsType<ConsentDto> = [
    { title: 'ID', dataIndex: 'id', width: 80, render: (v: number) => <CopyableText value={v} /> },
    { title: 'СНИЛС', dataIndex: 'patientSnils', width: 140, render: (v: string) => <CopyableText value={formatSnils(v)} /> },
    { title: 'Пациент', dataIndex: 'patientName', width: 220, render: (v: string) => <CopyableText value={v} /> },
    { title: 'Дата рождения', dataIndex: 'birthDate', width: 130, render: (v: string | null) => v ? dayjs(v).format('DD.MM.YYYY') : '-' },
    { title: 'Телефон', dataIndex: 'phone', width: 140, render: (v: string | null) => <CopyableText value={v} /> },
    { title: 'Статус', dataIndex: 'revokedAt', width: 120, render: (_v: string | null, record: ConsentDto) => {
      if (record.revokedAt) return <Tag color="red">Отозвано</Tag>;
      return record.isConsentGiven ? <Tag color="green">Активно</Tag> : <Tag color="orange">Не дано</Tag>;
    }},
    { title: 'Согласие', dataIndex: 'isConsentGiven', width: 110, render: (v: boolean) => v ? <Tag color="green">Дано</Tag> : <Tag color="red">Не дано</Tag> },
    { title: 'Вид согласия', dataIndex: 'consentType', width: 200, render: (v: string | null) => <CopyableText value={v} /> },
    { title: 'Дата создания', dataIndex: 'createdAt', width: 160, render: (v: string) => dayjs(v).format('DD.MM.YYYY HH:mm') },
    {
      title: 'Действия',
      key: 'actions',
      width: 180,
      render: (_, record) => (
        <Space>
          <Button
            icon={<PrinterOutlined />}
            size="small"
            onClick={() => handlePrint(record.id)}
          >
            PDF
          </Button>
          {!record.revokedAt && record.isConsentGiven && (
            <Button
              danger
              size="small"
              onClick={() => handleRevoke(record.id)}
            >
              Отозвать
            </Button>
          )}
        </Space>
      ),
    },
  ];

  const { columns, components } = useResizableColumns(initialColumns);

  const buildFilter = (page = 1, pageSize = pagination.pageSize): ConsentFilterRequest => {
    const values = form.getFieldsValue();
    const filter: ConsentFilterRequest = { page, pageSize };
    if (values.patientSnils) filter.patientSnils = values.patientSnils.replace(/\D/g, '');
    if (values.patientName) filter.patientName = values.patientName;
    if (values.isConsentGiven !== undefined && values.isConsentGiven !== null) filter.isConsentGiven = values.isConsentGiven;
    if (values.period && values.period.length === 2) {
      filter.dateFrom = values.period[0].format('YYYY-MM-DD');
      filter.dateTo = values.period[1].format('YYYY-MM-DD');
    }
    return filter;
  };

  const fetchData = useCallback(async (page = 1, pageSize = pagination.pageSize) => {
    setLoading(true);
    try {
      const filter = buildFilter(page, pageSize);
      const result = await getConsents(filter);
      setData(result.items);
      setPagination({ current: result.page, pageSize: result.pageSize, total: result.totalCount });
    } catch (error: any) {
      message.error(error.response?.data?.message || 'Ошибка загрузки данных');
    } finally {
      setLoading(false);
    }
  }, [form, pagination.pageSize]);

  const handleTableChange = (newPagination: any) => {
    setPagination((prev) => ({ ...prev, pageSize: newPagination.pageSize }));
    saveStoredFilters(form.getFieldsValue(), newPagination.pageSize);
    fetchData(newPagination.current, newPagination.pageSize);
  };

  const handleApply = () => {
    saveStoredFilters(form.getFieldsValue(), pagination.pageSize);
    fetchData(1);
  };

  const handleReset = () => {
    localStorage.removeItem(STORAGE_KEY);
    form.resetFields();
    setPagination((prev) => ({ ...prev, pageSize: DEFAULT_PAGE_SIZE }));
    fetchData(1, DEFAULT_PAGE_SIZE);
  };

  const handleLookup = async () => {
    const snils = modalForm.getFieldValue('patientSnils');
    if (!snils || snils.replace(/\D/g, '').length < 11) {
      message.warning('Введите корректный СНИЛС');
      return;
    }
    setLookupLoading(true);
    try {
      const patient = await lookupPatient(snils);
      setFoundPatient({
        name: patient.patientName,
        birthDate: patient.birthDate,
        phone: patient.phone,
      });
      modalForm.setFieldsValue({
        patientName: patient.patientName,
        birthDate: patient.birthDate ? dayjs(patient.birthDate) : null,
        phone: patient.phone,
      });
      message.success('Пациент найден');
    } catch (error: any) {
      setFoundPatient(null);
      message.error(error.response?.data?.message || 'Пациент не найден');
    } finally {
      setLookupLoading(false);
    }
  };

  const handleModalOk = async () => {
    try {
      const values = await modalForm.validateFields();
      const request: CreateConsentRequest = {
        patientSnils: values.patientSnils.replace(/\D/g, ''),
        patientName: values.patientName,
        birthDate: values.birthDate ? values.birthDate.format('YYYY-MM-DD') : null,
        phone: values.phone || null,
        isConsentGiven: values.isConsentGiven === true,
        consentType: values.consentType || null,
      };
      await createConsent(request);
      message.success('Согласие сохранено');
      setModalOpen(false);
      modalForm.resetFields();
      setFoundPatient(null);
      fetchData(1);
    } catch (error: any) {
      if (error.response?.data?.message) {
        message.error(error.response.data.message);
      }
    }
  };

  const handleModalCancel = () => {
    setModalOpen(false);
    modalForm.resetFields();
    setFoundPatient(null);
  };

  const handleRevoke = async (id: number) => {
    try {
      await revokeConsent(id);
      message.success('Согласие отозвано');
      fetchData(pagination.current, pagination.pageSize);
    } catch (error: any) {
      message.error(error.response?.data?.message || 'Ошибка отзыва согласия');
    }
  };

  const handlePrint = (id: number) => {
    const token = localStorage.getItem('token');
    const url = `${import.meta.env.VITE_API_URL || 'http://localhost:5000/api'}/consents/${id}/pdf`;
    fetch(url, {
      headers: token ? { Authorization: `Bearer ${token}` } : {},
    })
      .then((response) => {
        if (!response.ok) throw new Error('Ошибка печати');
        return response.blob();
      })
      .then((blob) => {
        const objectUrl = window.URL.createObjectURL(blob);
        window.open(objectUrl, '_blank');
        window.URL.revokeObjectURL(objectUrl);
      })
      .catch(() => message.error('Ошибка формирования PDF'));
  };

  return (
    <div style={{ padding: 24 }}>
      <Row justify="space-between" align="middle" style={{ marginBottom: 16 }}>
        <Col>
          <Title level={3} style={{ margin: 0 }}>Журнал согласий</Title>
        </Col>
        <Col>
          <Space>
            <span>{userName}</span>
            <Button onClick={() => navigate('/recipes')}>К рецептам</Button>
            <Button onClick={() => navigate('/journals')}>Журналы SMS</Button>
            <Button onClick={logout}>Выйти</Button>
          </Space>
        </Col>
      </Row>

      <Card style={{ marginBottom: 16 }}>
        <Form form={form} layout="vertical" onFinish={handleApply} initialValues={buildInitialValues(stored)}>
          <Row gutter={16}>
            <Col xs={24} md={8} lg={6}>
              <Form.Item name="patientSnils" label="СНИЛС">
                <Input
                  placeholder="000-000-000 00"
                  allowClear
                  maxLength={14}
                  onChange={(e) => form.setFieldsValue({ patientSnils: formatSnils(e.target.value) })}
                />
              </Form.Item>
            </Col>
            <Col xs={24} md={8} lg={6}>
              <Form.Item name="patientName" label="Пациент">
                <Input placeholder="ФИО" allowClear />
              </Form.Item>
            </Col>
            <Col xs={24} md={8} lg={6}>
              <Form.Item name="isConsentGiven" label="Согласие">
                <Select
                  allowClear
                  placeholder="Все"
                  options={[
                    { value: true, label: 'Дано' },
                    { value: false, label: 'Не дано' },
                  ]}
                />
              </Form.Item>
            </Col>
            <Col xs={24} md={8} lg={6}>
              <Form.Item name="period" label="Период создания">
                <RangePicker style={{ width: '100%' }} format="DD.MM.YYYY" />
              </Form.Item>
            </Col>
          </Row>
          <Row>
            <Col>
              <Space>
                <Button type="primary" htmlType="submit">Применить фильтры</Button>
                <Button onClick={handleReset}>Сбросить</Button>
              </Space>
            </Col>
          </Row>
        </Form>
      </Card>

      <Row style={{ marginBottom: 16 }}>
        <Col>
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setModalOpen(true)}>
            Добавить согласие
          </Button>
        </Col>
      </Row>

      <Table
        rowKey="id"
        columns={columns}
        components={components}
        dataSource={data}
        className="table-cell-wrap"
        loading={loading}
        pagination={{
          ...pagination,
          showSizeChanger: true,
          showTotal: (total) => `Всего: ${total}`,
        }}
        onChange={handleTableChange}
        scroll={{ x: 'max-content' }}
        size="small"
      />

      <Modal
        title="Добавление согласия"
        open={modalOpen}
        onOk={handleModalOk}
        onCancel={handleModalCancel}
        okText="Сохранить"
        cancelText="Отмена"
        width={640}
      >
        <Form form={modalForm} layout="vertical" initialValues={{ consentType: 'Согласие на рассылку' }}>
          <Row gutter={16}>
            <Col xs={24} md={16}>
              <Form.Item
                name="patientSnils"
                label="СНИЛС пациента"
                rules={[{ required: true, message: 'Введите СНИЛС' }]}
              >
                <Input
                  placeholder="000-000-000 00"
                  maxLength={14}
                  onChange={(e) => modalForm.setFieldsValue({ patientSnils: formatSnils(e.target.value) })}
                />
              </Form.Item>
            </Col>
            <Col xs={24} md={8}>
              <Button
                type="primary"
                icon={<SearchOutlined />}
                loading={lookupLoading}
                onClick={handleLookup}
                style={{ marginTop: 30, width: '100%' }}
              >
                Найти
              </Button>
            </Col>
          </Row>

          {foundPatient && (
            <Card size="small" style={{ marginBottom: 16, background: '#f6ffed' }}>
              <div><strong>Найден пациент:</strong></div>
              <div>{foundPatient.name}</div>
              {foundPatient.birthDate && <div>Дата рождения: {dayjs(foundPatient.birthDate).format('DD.MM.YYYY')}</div>}
              {foundPatient.phone && <div>Телефон: {foundPatient.phone}</div>}
            </Card>
          )}

          <Form.Item
            name="patientName"
            label="ФИО пациента"
            rules={[{ required: true, message: 'Введите ФИО' }]}
          >
            <Input placeholder="ФИО" />
          </Form.Item>

          <Row gutter={16}>
            <Col xs={24} md={12}>
              <Form.Item name="birthDate" label="Дата рождения">
                <DatePicker style={{ width: '100%' }} format="DD.MM.YYYY" />
              </Form.Item>
            </Col>
            <Col xs={24} md={12}>
              <Form.Item name="phone" label="Телефон">
                <Input placeholder="Телефон" />
              </Form.Item>
            </Col>
          </Row>

          <Form.Item name="consentType" label="Вид согласия">
            <Input placeholder="Например, Обработка персональных данных" />
          </Form.Item>

          <Form.Item
            name="isConsentGiven"
            valuePropName="checked"
            rules={[{ validator: (_, value) => value ? Promise.resolve() : Promise.reject(new Error('Необходимо поставить галочку согласия')) }]}
          >
            <Checkbox>Пациент дал согласие</Checkbox>
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
};

export default ConsentsPage;
