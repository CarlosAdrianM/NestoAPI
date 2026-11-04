/*
    NestoAPI#800 (04/11/26): el correo «Aviso descuadre en Agencia» (job del SQL Agent «Correos de Cuadres», paso
    prdCuadreAgenciasContabilidad) contaba como «pendientes de pago» los reembolsos DIFERIDOS a la entrega (NestoAPI#742):
    envíos tramitados con fila en EnviosAgenciaReembolsosAlEntregar y Asiento NULL, que todavía no tienen apunte en la
    cuenta 555 de la agencia porque no se han entregado. El 04/11 salían 70,87 € en la 55500042 (envío 250452, GLS) y
    287,36 € en la 55500092 (250406 + 250507, CTT): los tres estaban en reparto.

    Cambio: el lado de envíos excluye los que tienen el reembolso diferido y sin contabilizar. Al entregarse, el
    seguimiento contabiliza el reembolso y la fila pasa a tener Asiento, así que vuelven a contar (con su apunte).
    Los incidentados/devueltos con reembolso diferido que no se entregan nunca no se contabilizan (decisión de #742) y
    tampoco salen en el cuadre.

    El SP no está en el repo: vive en la BD. Ejecutar en SSMS contra NV como sa. Idempotente (ALTER).
    Resto del SP tal cual estaba (el cuerpo se copió de sys.sql_modules el 04/11/26).
*/

USE NV;
GO

IF OBJECT_ID('dbo.EnviosAgenciaReembolsosAlEntregar', 'U') IS NULL
BEGIN
    RAISERROR('Falta la tabla EnviosAgenciaReembolsosAlEntregar (Scripts/Issue742_ReembolsosAlEntregar.sql): no se cambia el SP.', 16, 1);
    SET NOEXEC ON;
END
GO

ALTER PROCEDURE [dbo].[prdCuadreAgenciasContabilidad]
AS
BEGIN
	-- SET NOCOUNT ON added to prevent extra result sets from
	-- interfering with SELECT statements.
	SET NOCOUNT ON;

    -- Insert statements for procedure here
	declare @datos as table (
		Empresa char(3),
		CuentaReembolsos char(10),
		Saldo money not null default(0)
	)

	DECLARE @tableHTML  NVARCHAR(MAX) ;

	-- Poblamos la tabla
	insert into @datos
	select Empresa, CuentaReembolsos, sum(importeAgencia) as Importe from
	(
		select a.Empresa, a.CuentaReembolsos, e.ImporteAgencia from AgenciasTransporte as a inner join
		(
			select Empresa, Agencia, sum(reembolso) as ImporteAgencia from EnviosAgencia
			where Estado > 0 and FechaPagoReembolso is null and Reembolso <> 0
			-- NestoAPI#800: el reembolso diferido a la entrega (#742) y aún sin contabilizar no está pendiente de pago
			and not exists (
				select 1 from EnviosAgenciaReembolsosAlEntregar as r
				where r.EnvioId = EnviosAgencia.Numero and r.Asiento is null
			)
			group by Empresa, Agencia
		) as e
		on a.Empresa = e.Empresa and a.Numero = e.Agencia
		union
		select Empresa, [Nº Cuenta], sum(-debe+haber) as ImporteContabilidad from Contabilidad where [Nº Cuenta] in (
			select CuentaReembolsos from AgenciasTransporte
		) and Fecha >= '01/01/19'
		group by Empresa, [Nº Cuenta]
	) as b
	group by Empresa, CuentaReembolsos having sum(importeagencia) <> 0

	-- Comprobamos que sumen cero y que sean correctos los datos
	if (select count(*) from @datos) > 0 begin
		-- Mandamos el correo
		SET @tableHTML =
			N'<h2>Hola</h2>' +
			N'<table border="1">' +
			N'<tr><th>Empresa</th>' +
			N'<th>Cuenta</th>' +
			N'<th>Saldo</th></tr>' +
			CAST ( ( SELECT td = Empresa,       '',
							td = CuentaReembolsos,       '',
							td = Saldo, ''
					from @datos
					FOR XML PATH('tr'), TYPE
			) AS NVARCHAR(MAX) ) +
			N'</table>' ;
			--set @tableHTML = '<h1>Hola</h1>';
		EXEC msdb.dbo.sp_send_dbmail
		@profile_name = 'Nesto',
		--@from_address = 'carlosadrian@nuevavision.es',
		@reply_to = 'administracion@nuevavision.es',
		@recipients = 'administracion@nuevavision.es',
		@body = @tableHTML,
		@body_format = 'HTML',
		@subject = 'Aviso descuadre en Agencia' ;
	end
END
GO
SET NOEXEC OFF;
GO

-- Comprobación (lo que mandaría el correo ahora, sin mandarlo): debe salir vacío si solo descuadraban los diferidos
select Empresa, CuentaReembolsos, sum(importeAgencia) as Saldo from
(
	select a.Empresa, a.CuentaReembolsos, e.ImporteAgencia from AgenciasTransporte as a inner join
	(
		select Empresa, Agencia, sum(reembolso) as ImporteAgencia from EnviosAgencia
		where Estado > 0 and FechaPagoReembolso is null and Reembolso <> 0
		and not exists (select 1 from EnviosAgenciaReembolsosAlEntregar as r where r.EnvioId = EnviosAgencia.Numero and r.Asiento is null)
		group by Empresa, Agencia
	) as e on a.Empresa = e.Empresa and a.Numero = e.Agencia
	union
	select Empresa, [Nº Cuenta], sum(-debe+haber) from Contabilidad where [Nº Cuenta] in (select CuentaReembolsos from AgenciasTransporte) and Fecha >= '01/01/19'
	group by Empresa, [Nº Cuenta]
) as b
group by Empresa, CuentaReembolsos having sum(importeagencia) <> 0;
GO
