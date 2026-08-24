import React from 'react';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { AuthProvider, useAuth } from './context/AuthContext';
import LoginPage from './pages/LoginPage';
import RecipesPage from './pages/RecipesPage';
import JournalsPage from './pages/JournalsPage';
import ConsentsPage from './pages/ConsentsPage';

const PrivateRoute: React.FC<{ element: React.ReactElement }> = ({ element }) => {
  const { isAuthenticated } = useAuth();
  return isAuthenticated ? element : <Navigate to="/login" replace />;
};

const App: React.FC = () => {
  return (
    <AuthProvider>
      <BrowserRouter>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route path="/recipes" element={<PrivateRoute element={<RecipesPage />} />} />
          <Route path="/journals" element={<PrivateRoute element={<JournalsPage />} />} />
          <Route path="/consents" element={<PrivateRoute element={<ConsentsPage />} />} />
          <Route path="/" element={<Navigate to="/recipes" replace />} />
        </Routes>
      </BrowserRouter>
    </AuthProvider>
  );
};

export default App;
