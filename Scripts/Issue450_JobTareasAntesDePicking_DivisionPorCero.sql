-- NestoAPI#450: el job del SQL Agent "Tareas Antes de Picking" (DC2016, L-V 10:00) falla a diario
-- desde el 07/08/26 en el paso 4 (MesesStockPorFamilias): "Error de división entre cero" al
-- calcular MesesStock = ValorStock / CosteVentas para una familia con ventas pero sin coste.
-- Desde entonces MesesStockPorFamilia no recibe filas.
--
-- Ejecutar en SSMS como sa contra DC2016\SQL2017 (msdb). Solo cambia el comando del paso 4:
-- NULLIF(c2.CosteVentas, 0) → la familia sin coste queda con MesesStock NULL en vez de tumbar el paso.
-- Es idempotente: se puede relanzar.

USE msdb;
GO

DECLARE @job_id uniqueidentifier = (SELECT job_id FROM dbo.sysjobs WHERE name = N'Tareas Antes de Picking');
IF @job_id IS NULL
BEGIN
    RAISERROR('No existe el job "Tareas Antes de Picking"', 16, 1);
    RETURN;
END

EXEC dbo.sp_update_jobstep
    @job_id = @job_id,
    @step_id = 4,
    @command = N'insert into MesesStockPorFamilia
select getdate() Fecha, c1.Familia, c1.[ValorStock], c2.CosteVentas,
       round(c1.[ValorStock] / NULLIF(c2.CosteVentas, 0) * 12, 2) MesesStock, -- NestoAPI#450: sin dividir por cero
	(select isnull(sum([base imponible]),0) from LinPedidoVta l where l.Estado = -1 and l.Familia = c1.Familia) as Pendientes
from (
       select Familia,sum(cantidad*preciomedio) as [ValorStock]
       from extractoproducto as e inner join productos as p
       on (e.empresa = ''1'' or e.empresa = ''3'') and (p.empresa = ''1'') and e.número = p.numero
       where (e.empresa = ''1'' or e.empresa = ''3'') and p.ficticio = 0
       group by Familia with rollup
       having sum(cantidad*preciomedio) <> 0
) as c1 inner join (
       select L.familia,isnull(sum(l.[base imponible]),0) VentasAño, sum(coste*cantidad) CosteVentas
       from cabfacturavta as c inner join linpedidovta as l
       on c.empresa=l.empresa and c.numero=l.[nº factura]
       where (c.empresa=''1'' or (c.empresa = ''3'' and c.origen = ''1'')) and l.[fecha factura] between dateadd(yy, -1, getdate()) and getdate() and grupo is not null --AND grupo <> ''CUR''
       group by L.familia
       having sum([base imponible]) <> 0
) as c2 on c1.Familia = c2.Familia
order by MesesStock';
GO

-- Comprobación: el paso 4 ya lleva el NULLIF
SELECT s.step_id, s.step_name, CASE WHEN s.command LIKE '%NULLIF(c2.CosteVentas, 0)%' THEN 'OK' ELSE 'SIN CAMBIAR' END AS estado
FROM dbo.sysjobs j JOIN dbo.sysjobsteps s ON s.job_id = j.job_id
WHERE j.name = N'Tareas Antes de Picking' AND s.step_id = 4;
GO

-- Opcional: lanzarlo ahora para recuperar la fila de hoy (manda también los 3 correos de los pasos 1-3).
-- EXEC msdb.dbo.sp_start_job @job_name = N'Tareas Antes de Picking';

-- Verificar mañana: SELECT MAX(Fecha) FROM NV.dbo.MesesStockPorFamilia  → hoy
-- y en sysjobhistory el paso 4 con run_status = 1.
