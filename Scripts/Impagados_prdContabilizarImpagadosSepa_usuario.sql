/*
 * Fix: contabilizar impagados con el usuario de Identity (no la cuenta de la API)
 * ------------------------------------------------------------------------------
 * Contexto (Nesto#340, modernización de la ventana de Remesas): al pasar el
 * "contabilizar impagados" de EF (que conectaba como el usuario de dominio,
 * NUEVAVISIONCarlos) a NestoAPI (que corre como NUEVAVISIONRDS2016$), el SP
 * llamaba a prdContabilizar SIN el 3er parámetro @Usuario. prdContabilizar, al no
 * recibirlo, cae en SYSTEM_USER = la cuenta de máquina, y valida las fechas
 * contables (prdFechaEsValida) contra ella -> aborta la contabilización.
 *
 * Cambios: se añade el parámetro @usuario y se reenvía a prdContabilizar. Cuando la
 * API lo pasa (usuario del JWT de empleado), prdContabilizar lo respeta porque la
 * conexion es cuenta de maquina ('$'); si va NULL, el comportamiento es el de antes.
 *
 * IMPORTANTE: el SP usa metodos del tipo XML (.value), que EXIGEN QUOTED_IDENTIFIER ON.
 * Ejecutar en SSMS con estas opciones (lección de los fixes de #294).
 *
 * BD: NestoConnection (NV). Es un ALTER: conserva los permisos existentes, no hace
 * falta re-GRANT a [NUEVAVISIONRDS2016$].
 */
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- =============================================
-- Author:		<Author,,Name>
-- Create date: <Create Date,,>
-- Description:	<Description,,>
-- =============================================
ALTER PROCEDURE [dbo].[prdContabilizarImpagadosSepa]
	-- Add the parameters for the stored procedure here
	@fichero xml,
	@usuario varchar(30) = NULL
AS
BEGIN
	-- SET NOCOUNT ON added to prevent extra result sets from
	-- interfering with SELECT statements.
	SET NOCOUNT ON;

    -- Insert statements for procedure here
	--declare @fichero as xml
declare @fecha as datetime
declare @cuentaTotal as int
declare @sumaTotal as money
declare @cuentaGrupo as int
declare @sumaGrupo as money
declare @remesa as varchar(35)
declare @importe as money
declare @endToEnd as varchar(35)
declare @CIF as char(9)
declare @cliente as char(10)
declare @contacto as char(3)
declare @empresa as char(3)
declare @codigoMotivo as char(4)
declare @descripcionMotivo as varchar(150)
declare @nuestroIBAN as varchar(34)
declare @cuentaBanco as char(10)
declare @gastosImpagado as money
declare @gastosImpagadoGrupo as money
declare @minGastosImpagado as money
declare @maxGastosImpagado as money
declare @porcGastosImpagado as decimal(5,4)
declare @concepto as varchar(140)
declare @documento as char(10)
declare @efecto as char(3)
declare @fechaVto as datetime
declare @tipoIVA as char(3)
declare @porcIVA as decimal(5,4)
declare @ruta as char(3)
declare @lineas as table
(
	tipoCuenta char(3),
	cliente char(10),
	contacto char(3),
	empresa char(3),
	debe money,
	haber money,
	codigoMotivo char(4),
	descripcionMotivo varchar(150),
	grupo int, 
	documento char(10),
	efecto char(3),
	fechaVto datetime, 
	ruta char(3),
	centroCoste char(3),
	vendedor char(3)
)

declare @importeMinimoImpagado as money = 3 -- Carlos 11/10/21
declare @cuentaGastosImpagado as char(10) = '62600000' -- Carlos 11/10/21

--set @fichero = '<Document xmlns="urn:iso:std:iso:20022:tech:xsd:pain.002.001.03"><CstmrPmtStsRpt><GrpHdr><MsgId>DAA78368255002201407011313280000093</MsgId><CreDtTm>2014-07-01T06:00:01</CreDtTm><InitgPty><Id><OrgId><BICOrBEI>CAIXESBBXXX</BICOrBEI></OrgId></Id></InitgPty></GrpHdr><OrgnlGrpInfAndSts><OrgnlMsgId>2014-07-01DEV0801005350A78368255002</OrgnlMsgId><OrgnlMsgNmId>pain.008</OrgnlMsgNmId><OrgnlNbOfTxs>000000000000005</OrgnlNbOfTxs><OrgnlCtrlSum>526.68</OrgnlCtrlSum><GrpSts>RJCT</GrpSts></OrgnlGrpInfAndSts><OrgnlPmtInfAndSts><OrgnlPmtInfId>A783682556405</OrgnlPmtInfId><OrgnlNbOfTxs>000000000000003</OrgnlNbOfTxs><OrgnlCtrlSum>410.11</OrgnlCtrlSum><PmtInfSts>RJCT</PmtInfSts><TxInfAndSts><OrgnlInstrId>078828588000000001</OrgnlInstrId><OrgnlEndToEndId>A78368255/19304/0/1585530</OrgnlEndToEndId><TxSts>RJCT</TxSts><StsRsnInf><Rsn><Cd>MD01</Cd></Rsn></StsRsnInf><OrgnlTxRef><Amt><InstdAmt Ccy="EUR">40.66</InstdAmt></Amt><ReqdColltnDt>2014-06-24</ReqdColltnDt><CdtrSchmeId><Id><PrvtId><Othr><Id>ES20002A78368255</Id><SchmeNm><Prtry>SEPA</Prtry></SchmeNm></Othr></PrvtId></Id></CdtrSchmeId><PmtTpInf><SvcLvl><Cd>SEPA</Cd></SvcLvl><LclInstrm><Cd>B2B</Cd></LclInstrm><SeqTp>RCUR</SeqTp><CtgyPurp><Cd>TRAD</Cd></CtgyPurp></PmtTpInf><MndtRltdInf><MndtId>1/19304/1</MndtId><DtOfSgntr>2009-10-31</DtOfSgntr><AmdmntInd>false</AmdmntInd></MndtRltdInf><RmtInf><Ustrd>Pago Factura NV1409424 Efecto 1 Vto. 24/06/14</Ustrd></RmtInf><Dbtr><Nm>CENTRO DE BELLEZA FLOR DE LIS S.L.</Nm><PstlAdr><Ctry>ES</Ctry></PstlAdr></Dbtr><DbtrAcct><Id><IBAN>ES7801281515100500002597</IBAN></Id></DbtrAcct><DbtrAgt><FinInstnId><BIC>BKBKESMMXXX</BIC></FinInstnId></DbtrAgt><CdtrAgt><FinInstnId><BIC>CAIXESBBXXX</BIC></FinInstnId></CdtrAgt><Cdtr><Nm>NUEVA VISION, S.A.</Nm><PstlAdr><Ctry>ES</Ctry></PstlAdr></Cdtr><CdtrAcct><Id><IBAN>ES0621006273900200063554</IBAN></Id></CdtrAcct></OrgnlTxRef></TxInfAndSts><TxInfAndSts>                     <OrgnlInstrId>078828588000000002</OrgnlInstrId><OrgnlEndToEndId>A78368255/19544/0/1585531</OrgnlEndToEndId><TxSts>RJCT</TxSts><StsRsnInf><Rsn><Cd>MS03</Cd></Rsn></StsRsnInf><OrgnlTxRef><Amt><InstdAmt Ccy="EUR">332.80</InstdAmt></Amt><ReqdColltnDt>2014-06-24</ReqdColltnDt><CdtrSchmeId><Id><PrvtId><Othr><Id>ES20002A78368255</Id><SchmeNm><Prtry>SEPA</Prtry></SchmeNm></Othr></PrvtId></Id></CdtrSchmeId><PmtTpInf><SvcLvl><Cd>SEPA</Cd></SvcLvl><LclInstrm><Cd>B2B</Cd></LclInstrm><SeqTp>RCUR</SeqTp><CtgyPurp><Cd>TRAD</Cd></CtgyPurp></PmtTpInf><MndtRltdInf><MndtId>1/19544/2</MndtId><DtOfSgntr>2009-10-31</DtOfSgntr><AmdmntInd>false</AmdmntInd></MndtRltdInf><RmtInf><Ustrd>Pago Factura NV1409425 Efecto 1 Vto. 24/06/14</Ustrd></RmtInf><Dbtr><Nm>VIVIAN PASTOR VALERO</Nm><PstlAdr><Ctry>ES</Ctry></PstlAdr></Dbtr><DbtrAcct><Id><IBAN>ES2701822654910201527866</IBAN></Id></DbtrAcct><DbtrAgt><FinInstnId><BIC>BBVAESMMXXX</BIC></FinInstnId></DbtrAgt><CdtrAgt><FinInstnId><BIC>CAIXESBBXXX</BIC></FinInstnId></CdtrAgt><Cdtr><Nm>NUEVA VISION, S.A.</Nm><PstlAdr><Ctry>ES</Ctry></PstlAdr></Cdtr><CdtrAcct><Id><IBAN>ES0621006273900200063554</IBAN></Id></CdtrAcct></OrgnlTxRef></TxInfAndSts><TxInfAndSts><OrgnlInstrId>078828588000000003</OrgnlInstrId><OrgnlEndToEndId>A78368255/20327/0/1585532</OrgnlEndToEndId><TxSts>RJCT</TxSts><StsRsnInf><Rsn><Cd>MD01</Cd></Rsn></StsRsnInf><OrgnlTxRef><Amt><InstdAmt Ccy="EUR">36.65</InstdAmt></Amt><ReqdColltnDt>2014-06-24</ReqdColltnDt><CdtrSchmeId><Id><PrvtId><Othr><Id>ES20002A78368255</Id><SchmeNm><Prtry>SEPA</Prtry></SchmeNm></Othr></PrvtId></Id></CdtrSchmeId><PmtTpInf><SvcLvl><Cd>SEPA</Cd></SvcLvl><LclInstrm><Cd>B2B</Cd></LclInstrm><SeqTp>RCUR</SeqTp><CtgyPurp><Cd>TRAD</Cd></CtgyPurp></PmtTpInf><MndtRltdInf><MndtId>1/20327/3</MndtId><DtOfSgntr>2009-10-31</DtOfSgntr><AmdmntInd>false</AmdmntInd></MndtRltdInf><RmtInf>                                                      <Ustrd>Pago Factura NV1409428 Efecto 1 Vto. 24/06/14</Ustrd></RmtInf><Dbtr><Nm>TAMARA JIMENEZ POLO</Nm><PstlAdr><Ctry>ES</Ctry></PstlAdr></Dbtr><DbtrAcct><Id><IBAN>ES3700815379780001100319</IBAN></Id></DbtrAcct><DbtrAgt><FinInstnId><BIC>BSABESBBXXX</BIC></FinInstnId></DbtrAgt><CdtrAgt><FinInstnId><BIC>CAIXESBBXXX</BIC></FinInstnId></CdtrAgt><Cdtr><Nm>NUEVA VISION, S.A.</Nm><PstlAdr><Ctry>ES</Ctry></PstlAdr></Cdtr><CdtrAcct><Id><IBAN>ES0621006273900200063554</IBAN></Id></CdtrAcct></OrgnlTxRef></TxInfAndSts></OrgnlPmtInfAndSts><OrgnlPmtInfAndSts><OrgnlPmtInfId>A783682556412</OrgnlPmtInfId><OrgnlNbOfTxs>000000000000001</OrgnlNbOfTxs><OrgnlCtrlSum>77.97</OrgnlCtrlSum><PmtInfSts>RJCT</PmtInfSts><TxInfAndSts><OrgnlInstrId>078859197000000008</OrgnlInstrId><OrgnlEndToEndId>A78368255/21945/0/1586018</OrgnlEndToEndId><TxSts>RJCT</TxSts><StsRsnInf><Rsn><Cd>MS03</Cd></Rsn></StsRsnInf><OrgnlTxRef><Amt><InstdAmt Ccy="EUR">77.97</InstdAmt></Amt><ReqdColltnDt>2014-06-25</ReqdColltnDt><CdtrSchmeId><Id><PrvtId><Othr><Id>ES20002A78368255</Id><SchmeNm><Prtry>SEPA</Prtry></SchmeNm></Othr></PrvtId></Id></CdtrSchmeId><PmtTpInf><SvcLvl><Cd>SEPA</Cd></SvcLvl><LclInstrm><Cd>B2B</Cd></LclInstrm><SeqTp>RCUR</SeqTp><CtgyPurp><Cd>TRAD</Cd></CtgyPurp></PmtTpInf><MndtRltdInf><MndtId>1/21945/2</MndtId><DtOfSgntr>2009-10-31</DtOfSgntr><AmdmntInd>false</AmdmntInd></MndtRltdInf><RmtInf><Ustrd>Pago Factura NV1409233 Efecto 1 Vto. 25/06/14</Ustrd></RmtInf><Dbtr><Nm>LUCERO PARIS, S.L.</Nm><PstlAdr><Ctry>ES</Ctry></PstlAdr></Dbtr><DbtrAcct><Id><IBAN>ES8901822251010201538409</IBAN></Id></DbtrAcct><DbtrAgt><FinInstnId><BIC>BBVAESMMXXX</BIC></FinInstnId></DbtrAgt><CdtrAgt><FinInstnId><BIC>CAIXESBBXXX</BIC></FinInstnId></CdtrAgt><Cdtr><Nm>NUEVA VISION, S.A.</Nm><PstlAdr><Ctry>ES</Ctry></PstlAdr></Cdtr><CdtrAcct><Id><IBAN>ES0621006273900200063554</IBAN></Id></CdtrAcct></OrgnlTxRef></TxInfAndSts>                   </OrgnlPmtInfAndSts><OrgnlPmtInfAndSts><OrgnlPmtInfId>A783682556414</OrgnlPmtInfId><OrgnlNbOfTxs>000000000000001</OrgnlNbOfTxs><OrgnlCtrlSum>38.60</OrgnlCtrlSum><PmtInfSts>RJCT</PmtInfSts><TxInfAndSts><OrgnlInstrId>078879522000000004</OrgnlInstrId><OrgnlEndToEndId>A78368255/16323/0/1586521</OrgnlEndToEndId><TxSts>RJCT</TxSts><StsRsnInf><Rsn><Cd>RC01</Cd></Rsn></StsRsnInf><OrgnlTxRef><Amt><InstdAmt Ccy="EUR">38.60</InstdAmt></Amt><ReqdColltnDt>2014-06-26</ReqdColltnDt><CdtrSchmeId><Id><PrvtId><Othr><Id>ES20002A78368255</Id><SchmeNm><Prtry>SEPA</Prtry></SchmeNm></Othr></PrvtId></Id></CdtrSchmeId><PmtTpInf><SvcLvl><Cd>SEPA</Cd></SvcLvl><LclInstrm><Cd>B2B</Cd></LclInstrm><SeqTp>RCUR</SeqTp><CtgyPurp><Cd>TRAD</Cd></CtgyPurp></PmtTpInf><MndtRltdInf><MndtId>1/16323/3</MndtId><DtOfSgntr>2014-06-18</DtOfSgntr><AmdmntInd>false</AmdmntInd></MndtRltdInf><RmtInf><Ustrd>Pago Factura NV1409633 Efecto 1 Vto. 26/06/14</Ustrd></RmtInf><Dbtr><Nm>MARTA HINOJAR ELEZ</Nm><PstlAdr><Ctry>ES</Ctry></PstlAdr></Dbtr><DbtrAcct><Id><IBAN>ES8814650100951800150925</IBAN></Id></DbtrAcct><DbtrAgt><FinInstnId><BIC>INGDESMMXXX</BIC></FinInstnId></DbtrAgt><CdtrAgt><FinInstnId><BIC>CAIXESBBXXX</BIC></FinInstnId></CdtrAgt><Cdtr><Nm>NUEVA VISION, S.A.</Nm><PstlAdr><Ctry>ES</Ctry></PstlAdr></Cdtr><CdtrAcct><Id><IBAN>ES0621006273900200063554</IBAN></Id></CdtrAcct></OrgnlTxRef></TxInfAndSts></OrgnlPmtInfAndSts></CstmrPmtStsRpt></Document>'
--SELECT @fichero for xml path('');


set @fecha = @fichero.value('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.002.001.03";
(/Document/CstmrPmtStsRpt/GrpHdr/CreDtTm)[1]','datetime')
set @cuentaTotal = @fichero.value('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.002.001.03";
(/Document/CstmrPmtStsRpt/OrgnlGrpInfAndSts/OrgnlNbOfTxs)[1]','int')
set @sumaTotal = @fichero.value('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.002.001.03";
(/Document/CstmrPmtStsRpt/OrgnlGrpInfAndSts/OrgnlCtrlSum)[1]','money') -- Esto es lo que tiene que ir a la 572

declare @i as int
declare @j as int
declare @grupo as int
declare @barra1 as int
declare @barra2 as int
declare @barra3 as int
set @i = 1
set @j = 1
set @grupo = 1
set @gastosImpagadoGrupo = 0 
while @i <= @cuentaTotal begin
	-- leemos los totales de grupo
	set @cuentaGrupo = @fichero.value('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.002.001.03";
	(/Document/CstmrPmtStsRpt/OrgnlPmtInfAndSts[position()=sql:variable("@grupo")]/OrgnlNbOfTxs)[1]','int')
	set @importe = @fichero.value('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.002.001.03";
	(/Document/CstmrPmtStsRpt/OrgnlPmtInfAndSts[position()=sql:variable("@grupo")]/TxInfAndSts[position()=sql:variable("@j")]/OrgnlTxRef/Amt/InstdAmt)[1]','money') -- Esto es lo que tiene que ir a la 572
	set @endToEnd = @fichero.value('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.002.001.03";
	(/Document/CstmrPmtStsRpt/OrgnlPmtInfAndSts[position()=sql:variable("@grupo")]/TxInfAndSts[position()=sql:variable("@j")]/OrgnlEndToEndId)[1]','varchar(35)') 
	set @barra1 = CHARINDEX('/', @endToEnd)
	set @barra2 = CHARINDEX('/', @endToEnd, @barra1+1)
	set @barra3 = CHARINDEX('/', @endToEnd, @barra2+1)
	set @CIF = left(@endToend, @barra1)
	set @cliente = SUBSTRING(@endToEnd, @barra1+1, @barra2-@barra1-1)
	set @contacto = SUBSTRING(@endToEnd, @barra2+1, @barra3-@barra2-1)
	select @empresa = Número from Empresas where [NIF]=@CIF
	-- Cogemos el IVA de la empresa 1, porque en cursos es gasto
	select @tipoIVA = TipoIvaDefecto from Empresas where Número = '1'
	select @porcIVA = [% IVA]/100 from ParámetrosIVA where Empresa = '1' and [IVA Cliente/Prov] = @tipoIVA and [IVA Producto]= @tipoIVA
		
	set @codigoMotivo = @fichero.value('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.002.001.03";
	(/Document/CstmrPmtStsRpt/OrgnlPmtInfAndSts[position()=sql:variable("@grupo")]/TxInfAndSts[position()=sql:variable("@j")]/StsRsnInf/Rsn/Cd)[1]','varchar(4)')

	select @descripcionMotivo = case @codigoMotivo
	when 'AC01' then 'Número de cuenta incorrecto (IBAN no válido).'
	when 'AC04' then 'Cuenta cancelada.'
	when 'AC06' then 'Cuenta bloqueada y/o cuenta bloqueada por el deudor para adeudos directos.'
	when 'AC13' then 'Cuenta del deudor es cuenta de un consumidor.'
	when 'AG01' then 'Cuenta no admite adeudos directos.'
	when 'AG02' then 'Código de operación incorrecto.'
	when 'AM04' then 'Saldo insuficiente.'
	when 'AM05' then 'Operación duplicada.'
	when 'BE01' then 'Titular de la cuenta de cargo no coincide con el deudor.'
	when 'BE05' then 'Identificador del acreedor incorrecto.'
	when 'FF01' then 'Formato no válido.'
	when 'FF05' then 'Código de operación o tipo de código de transacción incorrecto'
	when 'MD01' then 'Mandato no válido o inexistente.'
	when 'MD02' then 'Faltan datos del mandato o son incorrectos.'
	when 'MD06' then 'Transacción autorizada disconforme'
	when 'MD07' then 'Deudor fallecido.'
	when 'MS02' then 'Razón no especificada por el cliente (orden del deudor).'
	when 'MS03' then 'Razón no especificada por la entidad del deudor.'
	when 'RC01' then 'Identificador de la entidad incorrecto (BIC no válido).'
	when 'RR01' then 'Falta identificación o cuenta del deudor. Razones regulatorias.'
	when 'RR02' then 'Falta nombre o dirección del deudor. Razones regulatorias.'
	when 'RR03' then 'Falta nombre o dirección del acreedor. Razones regulatorias.'
	when 'RR04' then 'Razones regulatorias.'
	when 'SL01' then 'Servicios específicos ofrecidos por la entidad del deudor.'
	else
		'Motivo no relacionado en SEPA' 
	end
	
	-- Buscamos el nº documento
	set @concepto = @fichero.value('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.002.001.03";
	(/Document/CstmrPmtStsRpt/OrgnlPmtInfAndSts[position()=sql:variable("@grupo")]/TxInfAndSts[position()=sql:variable("@j")]/OrgnlTxRef/RmtInf/Ustrd)[1]','varchar(140)')
	if CHARINDEX(' ',@concepto, 14) <> 14 begin --si el documento y efecto están en blanco
		set @documento = SUBSTRING(@concepto, 14, CHARINDEX(' ',@concepto, 14)-14)--SUBSTRING(@concepto, 14, 10)
		set @efecto = SUBSTRING(@concepto, 31, CHARINDEX(' ',@concepto, 31)-31)
		--set @fechaVto = cast(SUBSTRING(@concepto, 38, 8) as datetime)
	end else begin
		set @documento = ''
		set @efecto = ''
	end
	
	set @fechaVto = cast(SUBSTRING(@concepto, CHARINDEX('Vto. ',@concepto, 1)+5, 8) as datetime)
	
	-- Buscamos la ruta del cliente
	--select @ruta = Ruta from clientes where Empresa = @empresa and [Nº Cliente] = @cliente and Contacto = @contacto
	select @ruta = '06' --ruta de impagados

	-- Insertamos el apunte del cliente
	insert into @lineas
	select '2', @cliente, @contacto, @empresa, @importe, 0, @codigoMotivo, @descripcionMotivo, @grupo, @documento, @efecto, @fechaVto, @ruta, null, null

	-- Buscamos la cuenta del banco
	set @nuestroIBAN = @fichero.value('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.002.001.03";
	(/Document/CstmrPmtStsRpt/OrgnlPmtInfAndSts[position()=sql:variable("@grupo")]/TxInfAndSts[position()=sql:variable("@j")]/OrgnlTxRef/CdtrAcct/Id/IBAN)[1]','varchar(34)')
	select @cuentaBanco = [Cuenta Contable] from Bancos where Empresa = @empresa and Entidad=SUBSTRING(@nuestroIBAN, 5,4) and Sucursal = SUBSTRING(@nuestroIBAN, 9,4) and DC = SUBSTRING(@nuestroIBAN, 13,2) and [Nº Cuenta]=SUBSTRING(@nuestroIBAN, 15,10)
		
	
	-- Insertamos el apunte de gastos en el cliente
	/* Carlos 02/07/14 -> comentarios campos banco:
	- ImporteComisiónMáxImpagado -> importe en EUR máximo que cobran por un impagado (+ IVA)
	- ImporteCorreoImpagado -> no lo usamos de momento
	- ImporteComisiónPorDevolucion -> % de comisión que cobran entre el mínimo y el máximo (+ IVA)
	- GastosImpagado -> importe mínimo que cobran por impagado en EUR (+ IVA)
	*/
	-- Carlos 11/10/21: @minGastosImpagado es lo mínimo que nos cobra el banco a nosotros. @importeMinimoImpagado es lo mínimo que cobramos nosotros al cliente
	select @minGastosImpagado = GastosImpagado, @maxGastosImpagado = ImporteComisiónMáxImpagado, @porcGastosImpagado = ImporteComisiónPorDevolucion/100 from Bancos where Empresa = @empresa and [Cuenta Contable] = @cuentaBanco
	set @gastosImpagado = @importe * @porcGastosImpagado
	if @gastosImpagado > @maxGastosImpagado
		set @gastosImpagado = @maxGastosImpagado
	else if @gastosImpagado < @minGastosImpagado
		set @gastosImpagado = @minGastosImpagado
		
	set @gastosImpagado = round(@gastosImpagado * (1+ @porcIVA),2)
	set @gastosImpagadoGrupo = @gastosImpagadoGrupo + @gastosImpagado

	if (@gastosImpagado >= @importeMinimoImpagado) begin
		insert into @lineas
			select '2', @cliente, @contacto, @empresa, @gastosImpagado, 0, @codigoMotivo, 'Gastos Impagado '+RTRIM(@documento)+'/'+RTRIM(@efecto)+' ('+RTRIM(@codigoMotivo)+')'  , @grupo, @documento, @efecto, @fechaVto, @ruta, null, null
	end else begin
		insert into @lineas
			select '2', @cliente, @contacto, @empresa, @importeMinimoImpagado, 0, @codigoMotivo, 'Gastos Impagado '+RTRIM(@documento)+'/'+RTRIM(@efecto)+' ('+RTRIM(@codigoMotivo)+')'  , @grupo, @documento, @efecto, @fechaVto, @ruta, null, null
		insert into @lineas
			select '1', @cuentaGastosImpagado, null, @empresa, 0, @importeMinimoImpagado - @gastosImpagado, @codigoMotivo, left('Recupero Gastos Impagado '+RTRIM(@documento)+'/'+RTRIM(@efecto)+' ('+RTRIM(@codigoMotivo)+')',50)  , @grupo, @documento, @efecto, @fechaVto, @ruta, 'CA', null
	end



	set @i = @i+1
	if @j>=@cuentaGrupo begin -- Total de grupo
		set @sumaGrupo = @fichero.value('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.002.001.03";
		(/Document/CstmrPmtStsRpt/OrgnlPmtInfAndSts[position()=sql:variable("@grupo")]/OrgnlCtrlSum)[1]','money') 
		set @remesa = @fichero.value('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.002.001.03";
		(/Document/CstmrPmtStsRpt/OrgnlPmtInfAndSts[position()=sql:variable("@grupo")]/OrgnlPmtInfId)[1]','varchar(35)') 
		set @remesa = SUBSTRING(@remesa, 10, 10)
		-- Intertamos el apunte del banco
		insert into @lineas
		select '1', @cuentaBanco, null, @empresa, 0, @sumaGrupo, null, rtrim(cast(@j as char))+' Impagado(s) SEPA' , @grupo, @remesa, null, null, null, null, null

		-- Insertamos el apunte de gastos en el banco
		insert into @lineas
		select '1', @cuentaBanco, null, @empresa, 0, @gastosImpagadoGrupo, null, 'Gastos ' + rtrim(cast(@j as char))+' impagado(s) SEPA' , @grupo, @remesa, null, null, null, null, null

		set @gastosImpagadoGrupo = 0
		set @j = 1
		set @grupo = @grupo + 1
		
	end else
		set @j = @j+1
end

-- Carlos Adrián Martínez: 20/06/24. Ponemos el vendedor original de la factura.
UPDATE l
SET l.vendedor = ec.Vendedor
FROM @lineas l
OUTER APPLY (
    SELECT TOP 1 Vendedor
    FROM ExtractoCliente ec
    WHERE ec.Empresa = l.empresa
      AND ec.[Nº Documento] = l.documento
      AND ec.TipoApunte = 1
    ORDER BY ec.[Nº Orden]
) ec;


/**
 * TRANSACCIÓN 
 */
begin transaction


-- Contabilizamos
insert into PreContabilidad (Empresa, TipoApunte, TipoCuenta, [Nº Cuenta], Contacto, Concepto, Debe, Haber, Fecha, [Nº Documento], Efecto, Asiento, Diario, [Asiento Automático], Delegación, FormaVenta, FechaVto, Ruta, FormaPago, Departamento, CentroCoste, Vendedor)
select empresa, '4', tipoCuenta, cliente, contacto, left(descripcionMotivo, 50), debe, haber, CAST(CONVERT(CHAR(8), @fecha, 112) AS datetime), documento, efecto, grupo, '_ImpagAuto', 0, 'ALG', 'VAR', fechaVto, ruta, 'EFC', 'ADM', centroCoste, vendedor from @lineas

--select * from PreContabilidad where Diario = '_ImpagAuto' order by [Nº Orden]
--delete PreContabilidad where Diario = '_ImpagAuto'
exec prdContabilizar @empresa, '_ImpagAuto', @usuario
if @@Error!=0 begin
	raiserror('Error al Contabilizar Impagados',11,1)
	rollback transaction
	return
end else
	commit transaction
END

