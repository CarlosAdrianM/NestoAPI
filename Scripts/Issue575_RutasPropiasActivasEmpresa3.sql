/*
    Sugerencia 575 (Alfredo, almacén; aprobada por Carlos el 13/10/26): marcar como ACTIVAS las rutas propias AT
    («Atención técnica») y 16 («Ruta diaria») de la empresa 3, que se usan a diario pero estaban con Activa = 0
    (en la empresa 1 ya están activas).

    Ejecutar en SSMS contra NV como sa. Idempotente: solo toca las filas que están inactivas.

    Qué mira Activa (revisado el 13/10/26, solo lectura):
      - Nesto (WPF) y NestoAPI: NADA. Ningún selector, validación, picking ni facturación de rutas lee Rutas.Activa
        (el picking filtra por número de ruta; la facturación de rutas usa la lista fija AT/16 de RutaPropia y solo
        comprueba que la ruta exista; los portes, GestorImportesMinimos.esRutaConPortes, también por número).
      - Procedimientos de SQL que filtran «ruta in (select numero from rutas where empresa = @empresa and activa = 1)»:
          * prdInformeErroresRuta @Empresa      -> con @Empresa = '3' empezarán a salir los errores de AT y 16.
          * prdInformePendientesEnReposicion @Empresa -> con '3' contará el pendiente de los pedidos AT/16.
          * prdComisionesPorVendedor2017 (vía prdComisionesPorVendedor) -> con @Empresa = '3' contaría lo «en curso»
            de AT/16; la API (comisiones antiguas) lo llama con la empresa 1, así que no cambia nada.
          * prdInformeControlPedidos usa las rutas de la empresa 1 (empresa = '1'): no cambia.
        Ninguno de los dos primeros se llama desde Nesto ni desde la API (serán del Nesto viejo, si acaso).
      - El Nesto viejo (VB6) no está en ningún repositorio: si algún desplegable suyo filtrara por Activa, AT y 16 de la
        empresa 3 aparecerían ahí a partir de ahora (hoy Alfredo ya las pone, p. ej. el 928241, así que no parece filtrar).
    No se toca nada más de la fila (ImporteMinimo 25 y Copias 'C' de la empresa 3 se quedan como están).
*/

SET NOCOUNT ON;
USE NV;
GO

SELECT 'ANTES' AS Momento, Empresa, Número, Descripción, Activa FROM dbo.Rutas
 WHERE Empresa IN ('1', '3') AND Número IN ('AT', '16') ORDER BY Empresa, Número;

UPDATE dbo.Rutas
   SET Activa = 1, Usuario = LEFT(SUSER_SNAME(), 30), [Fecha Modificación] = GETDATE()
 WHERE Empresa = '3' AND Número IN ('AT', '16') AND Activa = 0;

SELECT @@ROWCOUNT AS RutasActivadas;

SELECT 'DESPUÉS' AS Momento, Empresa, Número, Descripción, Activa FROM dbo.Rutas
 WHERE Empresa IN ('1', '3') AND Número IN ('AT', '16') ORDER BY Empresa, Número;
GO

-- Rutas: public ya tiene SELECT; se concede expresamente a la API (regla de los scripts de NestoConnection).
GRANT SELECT ON dbo.Rutas TO [NUEVAVISION\RDS2016$];
GO
