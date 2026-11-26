/*
    Novedad 1453 (Laura, 26/11/26): la cuenta 70500009 «Ingresos por publicidad en YouTube» no tiene Tipo de IVA en el plan de
    cuentas, y por eso Nesto no deja crear el pedido de Google (cliente 32203, exento): «la línea no tiene Tipo de IVA».
    Los pedidos de los meses anteriores (922214, 923114, 924470, 927011) llevan G21 en esa línea (con el cliente EX, 0 %).
    Se le pone G21 a la cuenta. Lanzar como sa en la BD NV. Se puede lanzar más de una vez.
*/
USE NV;
GO
UPDATE PlanCuentas SET IVA = 'G21' WHERE Empresa = '1' AND [Nº Cuenta] = '70500009' AND NULLIF(RTRIM(IVA), '') IS NULL;
GO
SELECT RTRIM([Nº Cuenta]) AS Cuenta, RTRIM(Concepto) AS Concepto, RTRIM(IVA) AS IVA FROM PlanCuentas WHERE Empresa = '1' AND [Nº Cuenta] = '70500009';
GO
