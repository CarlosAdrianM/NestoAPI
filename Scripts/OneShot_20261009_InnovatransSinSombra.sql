/*
    09/10/26: Innovatrans (agencia 12) deja de estar marcada como sombra.

    El 08/10 se puso EsSombra = 1 como parche para que el comparador de agencias de la API (ParaSeleccion) no la eligiera,
    porque entonces solo excluía las sombra y no las que están en AgenciasEnCuarentena. Desde NestoAPI#607 (2f62b6b5,
    publicado el 08/10) el comparador también excluye la cuarentena, así que el parche sobra: Innovatrans vuelve a ser una
    agencia normal que sigue FUERA de la selección por estar en cuarentena (las 18 filas de AgenciasEnCuarentena de Nesto
    no se tocan). El coste se sigue calculando para la comparativa.

    Comprobar después: en Nesto, la «más económica» de un envío nacional no propone Innovatrans.
    Vuelta atrás: el mismo UPDATE con EsSombra = 1. SSMS contra NV, como sa.
*/
SET NOCOUNT ON;
USE NV;
GO

SELECT 'Antes' Momento, Empresa, Numero, Nombre, EsSombra FROM AgenciasTransporte WHERE Numero = 12;

UPDATE AgenciasTransporte SET EsSombra = 0 WHERE Numero = 12 AND EsSombra = 1;
SELECT @@ROWCOUNT Cambiadas;   -- 1 (0 si ya estaba hecho)

SELECT 'Después' Momento, Empresa, Numero, Nombre, EsSombra FROM AgenciasTransporte WHERE Numero = 12;
