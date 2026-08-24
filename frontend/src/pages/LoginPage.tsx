import React, { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { Form, Input, Button, Card, message, Typography, Select } from 'antd';
import { login, getUsers, type UserListItem } from '../api/auth';
import { useAuth } from '../context/AuthContext';

const { Title } = Typography;

const LoginPage: React.FC = () => {
  const [loading, setLoading] = useState(false);
  const [users, setUsers] = useState<UserListItem[]>([]);
  const [usersLoading, setUsersLoading] = useState(true);
  const navigate = useNavigate();
  const { login: authLogin } = useAuth();

  useEffect(() => {
    getUsers()
      .then(setUsers)
      .catch(() => message.error('Не удалось загрузить список пользователей'))
      .finally(() => setUsersLoading(false));
  }, []);

  const onFinish = async (values: { login: string; password: string }) => {
    setLoading(true);
    try {
      const response = await login(values);
      authLogin(response.token, response.userName);
      message.success('Успешный вход');
      navigate('/recipes');
    } catch (error: any) {
      message.error(error.response?.data?.message || 'Ошибка авторизации');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', minHeight: '100vh', background: '#f0f2f5' }}>
      <Card style={{ width: 400 }}>
        <Title level={3} style={{ textAlign: 'center' }}>SMS Рецепты</Title>
        <Form name="login" onFinish={onFinish} layout="vertical">
          <Form.Item
            label="Пользователь"
            name="login"
            rules={[{ required: true, message: 'Выберите пользователя' }]}
          >
            <Select
              showSearch
              placeholder="Выберите пользователя"
              loading={usersLoading}
              filterOption={(input, option) =>
                (option?.label ?? '').toLowerCase().includes(input.toLowerCase())
              }
              options={users.map((u) => ({ value: u.code, label: u.name }))}
            />
          </Form.Item>
          <Form.Item
            label="Пароль"
            name="password"
            rules={[{ required: true, message: 'Введите пароль' }]}
          >
            <Input.Password />
          </Form.Item>
          <Form.Item>
            <Button type="primary" htmlType="submit" loading={loading} block>
              Войти
            </Button>
          </Form.Item>
        </Form>
      </Card>
    </div>
  );
};

export default LoginPage;
