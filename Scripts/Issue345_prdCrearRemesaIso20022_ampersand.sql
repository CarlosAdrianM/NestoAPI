/*
 * Fix: '&' en nombres del fichero SEPA (NestoAPI#345 / remesas)
 * -----------------------------------------------------------
 * prdCrearRemesaIso20022 escapaba '&' a '&amp;'/'&amp' y luego hacía UPPER(), con lo que salía
 * '&AMP;'/'&AMP' en el nombre (p.ej. "HEALTHY & FIT" -> "HEALTHY &AMP FIT"). Ademas '&' NO esta
 * en el juego de caracteres SEPA. Decision (Carlos): transliterar '&' -> 'Y' en el nombre del
 * ACREEDOR (@nombre) y del DEUDOR (@nombreCliente). Asi no hay '&' ni entidad rota.
 *
 * IMPORTANTE: el SP usa metodos XML (.modify/.value), que EXIGEN QUOTED_IDENTIFIER ON. Ejecutar
 * en SSMS con estas opciones. Es un ALTER: conserva permisos (GRANT a [NUEVAVISIONRDS2016$] ya
 * presente), no re-GRANT.
 *
 * Pendiente (sibling, NO tocado aqui): '<','>','"' y sobre todo la comilla (') tienen el mismo
 * doble-escape (-> &APOS...). Tampoco estan en el charset SEPA. Se deja para una limpieza aparte.
 */
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- =============================================
-- Author:		Carlos Adrián Martínez
-- Create date: 12/02/14
-- Description:	Fichero para crear las remesas en formato SEPA B2B en norma ISO 20022
-- =============================================
ALTER PROCEDURE [dbo].[prdCrearRemesaIso20022]
	-- Add the parameters for the stored procedure here
	@remesa as int, 
	@codigo as char(4) = NULL,
	@fechaCobro as datetime = NULL
AS
BEGIN
	-- SET NOCOUNT ON added to prevent extra result sets from
	-- interfering with SELECT statements.
	SET NOCOUNT ON;
	SET ARITHABORT ON;
--declare @remesa as integer = 6391 --6017 --5941

/*
-- Comprobación de remesa
select e.[nº orden], e.Numero, e.contacto, -e.Importe, e.[CIF/NIF], c.bic, c.pais+c.dc_iban+c.entidad+c.oficina+c.dc+c.[nº cuenta]
from ExtractoCliente as e inner join ccc as c
on e.empresa = c.empresa and e.numero = c.cliente and e.contacto = c.contacto and e.ccc = c.numero
where e.Remesa = 6010 and e.TipoApunte = 3 -- pago

*/

--declare @PAIN as varchar(100) = 'urn:iso:std:iso:20022:tech:xsd:pain.008.001.02';

/****
 * Constantes pendientes de creación en BBDD
 */
--declare @BIC_OR_BEI_EMPRESA as nvarchar(11) = 'CAIXESBBXXX';
--declare @IBAN_EMPRESA as nvarchar(34) = 'ES0621006273900200063554'


/****
 * Declaración de variables
 */
DECLARE @myDoc xml;
declare @valor as nvarchar(255); --variable auxiliar para varios valores
declare @fecha as datetime;
declare @valorxml as xml; -- variable en formato XML para insert
declare @numOrden as int 
declare @numCliente as nvarchar(15) 
declare @contacto as nvarchar(3) 
declare @importe as money 
declare @fechaVto as datetime 
declare @empresa as char(3);
declare @nombre as nvarchar(70);
declare @cif as nvarchar(20);
declare @cifIntl as nvarchar(20);
declare @cifCliente as nvarchar(20);
declare @id_movimiento as nvarchar(35);
declare @bicEmpresa as nvarchar(11);
declare @ibanEmpresa as nvarchar(24);
declare @bicCliente as nvarchar(11);
declare @ibanCliente as nvarchar(24);
declare @cccCliente as char(3);
declare @nombreCliente as char(70);
declare @numDocumento as char(10);
declare @numEfecto as char(3);
declare @fechaMandato as datetime;
declare @fechaUltCobro as datetime; -- Carlos 28/08/14, para hacer el RCUR y el FRST
declare @secuencia as varchar(4)
declare @clienteError as varchar(15)
declare @contactoError1 as varchar(3)
declare @contactoError2 as varchar(3)
declare @fechaCobroGrupo as date


-- Lo primero vemos si un contacto se queda con FRST
select @clienteError= rtrim(i.Cliente), @contactoError1= rtrim(i.Contacto), @contactoError2= rtrim(c.Contacto)
from ccc as i inner join ccc as c
on i.Empresa = c.Empresa and i.Cliente = c.Cliente and i.Contacto <> c.Contacto and i.Número = c.Número and i.Estado >= 0 and c.Estado >= 0
where i.Secuencia <> c.Secuencia 
if @clienteError is not null begin
	raiserror('Los contactos %s y %s del cliente %s tienen secuencias diferentes.', 16, 1, @contactoError1, @contactoError2, @clienteError)
	return
end

-- Recogemos datos iniciales
--(Carlos 22/07/22: quito fechaVto y la cojo línea a línea)select @empresa=r.Empresa, @fechaVto=r.Fecha, @bicEmpresa = rtrim(b.BIC), @ibanEmpresa=b.pais+b.dc_iban+b.Entidad+b.Sucursal+b.DC+b.[Nº Cuenta]
select @empresa=r.Empresa, @bicEmpresa = rtrim(b.BIC), @ibanEmpresa=b.pais+b.dc_iban+b.Entidad+b.Sucursal+b.DC+b.[Nº Cuenta]
from remesas as r inner join Bancos as b
on r.Empresa= b.Empresa and r.Banco=b.Número
where r.numero = @remesa;

select @nombre = rtrim(nombre), @cif = nif from empresas where numero = @empresa;
select @nombre = replace(@nombre, '&', 'Y');
select @nombre = replace(@nombre, '<', '&lt;');
select @nombre = replace(@nombre, '>', '&gt;');
select @nombre = replace(@nombre, '"', '&quot;');
select @nombre = replace(@nombre, '''', '&apos;');
select @nombre = UPPER(@nombre);
select @nombre = replace(@nombre, 'Á', 'A');
select @nombre = replace(@nombre, 'É', 'E');
select @nombre = replace(@nombre, 'Í', 'I');
select @nombre = replace(@nombre, 'Ó', 'O');
select @nombre = replace(@nombre, 'Ú', 'U');
select @nombre = replace(@nombre, '`', '&apos;');
select @nombre = replace(@nombre, '´', '&apos;');

-- Inicializamos fechaCobro si no se ha pasado por parámetro
if @fechaCobro is null
	set @fechaCobro = GETDATE()

-- Inicializamos el Código, si no se ha pasado por parámetro
if @codigo is null begin
	if @empresa = '2'
		set @codigo = 'CORE'
	else
		set @codigo = 'B2B'
end


-- Mientras no tengamos los datos en la BBDD lo ponemos en código
if @empresa='1'
	set @cifIntl = 'ES20002A78368255'
else if @empresa = '2'
	set @cifIntl = 'ES55001B80875479'
else if @empresa = '4'
	set @cifIntl = 'ES32001B84251396'
else if @empresa = '5'
	set @cifIntl = 'ES59001B84365758'
else begin
	raiserror('Empresa no válida',16,1)
	return
end


/*
Cálculo dígitos de control
1. Tomamos posiciones de la 8 a la 15: 
A12345678
2. Añadimos ES y 00: A12345678ES00
3. Convertimos letras a números (según tabla 
cuaderno): 1012345678142800
4. Aplicamos modelo 97-10 (dado un nº, lo 
dividimos entre 97 y restamos a 98 el resto de 
la operación. Si se obtinene un único dígito, se 
completa con un cero por delante): 53
*/



-- Creamos la instancia
--SET @myDoc = '<?xml version="1.0" encoding="UTF-8"?><Document xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns="urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"><>'
--SET @myDoc = '<?xml version="1.0" encoding="UTF-8"?><Document xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"><>'
--set @myDoc='<CstmrDrctDbtInitn></CstmrDrctDbtInitn>'
--set @myDoc='<CstmrDrctDbtInitn xmlns="urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"></CstmrDrctDbtInitn>'

SELECT @myDoc=''
/*
set @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; insert <CstmrDrctDbtInitn></CstmrDrctDbtInitn> 
into ()[1]') ;
*/

/*********************
 * Cabecera
 ***/
--WITH XMLNAMESPACES (default 'urn:iso:std:iso:20022:tech:xsd:pain.008.001.02')
set @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
insert <Document xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"><CstmrDrctDbtInitn></CstmrDrctDbtInitn></Document>
into (/)[1]') ;

 
-- Insertamos Cabecera
set @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
insert <GrpHdr><MsgId>Remesa</MsgId><CreDtTm>FechaYHora</CreDtTm><NbOfTxs>Operaciones</NbOfTxs><CtrlSum>ControlDeSuma</CtrlSum><InitgPty></InitgPty></GrpHdr> 
into (/Document/CstmrDrctDbtInitn)[1]') ;

/*
-- Insertamos Identificación del mensaje (nº remesa)
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
insert <MsgId>Remesa</MsgId>
into   (/Document/CstmrDrctDbtInitn/GrpHdr)[1]');
*/

set @valor = 'Remesa de Recibos ' + rtrim(cast(@remesa as CHAR));
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
  replace value of (/Document/CstmrDrctDbtInitn/GrpHdr/MsgId/text())[1]
  with     sql:variable("@valor")
')
/*
-- Insertamos fecha y hora de creación
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
insert <CreDtTm>FechaYHora</CreDtTm>
into   (/Document/CstmrDrctDbtInitn/GrpHdr)[1]');
*/
--set @fecha = (select fecha from remesas where numero = @remesa);
set @fecha = getdate();
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
  replace value of (/Document/CstmrDrctDbtInitn/GrpHdr/CreDtTm/text())[1]
  with     sql:variable("@fecha")
')

/*
-- Insertamos número de operaciones
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
insert <NbOfTxs>Operaciones</NbOfTxs>
into   (/Document/CstmrDrctDbtInitn/GrpHdr)[1]');
*/
set @valor = (select isnull(count(*),0) from extractocliente where tipoapunte = 3 and remesa = @remesa);
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
  replace value of (/Document/CstmrDrctDbtInitn/GrpHdr/NbOfTxs/text())[1]
  with     sql:variable("@valor")
')
/*
-- Insertamos control de suma
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
insert <CtrlSum>ControlDeSuma</CtrlSum>
into   (/Document/CstmrDrctDbtInitn/GrpHdr)[1]');
*/
set @valor = (select importe from remesas where numero = @remesa);
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
  replace value of (/Document/CstmrDrctDbtInitn/GrpHdr/CtrlSum/text())[1]
  with     sql:variable("@valor")
')
/*
-- Insertamos Parte Iniciadora
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
insert <InitgPty></InitgPty>
into   (/Document/CstmrDrctDbtInitn/GrpHdr)[1]');
*/
-- Insertamos nombre
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
insert <Nm>Nombre</Nm>
into   (/Document/CstmrDrctDbtInitn/GrpHdr/InitgPty)[1]');

set @valor = left(@nombre,70);
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
  replace value of (/Document/CstmrDrctDbtInitn/GrpHdr/InitgPty/Nm/text())[1]
  with     sql:variable("@valor")
')

-- Insertamos Identificación
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
insert <Id></Id>
into   (/Document/CstmrDrctDbtInitn/GrpHdr/InitgPty)[1]');

-- Insertamos Persona Jurídica
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
insert <OrgId></OrgId>
into   (/Document/CstmrDrctDbtInitn/GrpHdr/InitgPty/Id)[1]');

-- Insertamos Otra
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
insert <Othr></Othr>
into   (/Document/CstmrDrctDbtInitn/GrpHdr/InitgPty/Id/OrgId)[1]');

-- Insertamos Identificación
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
insert <Id>Identificacion</Id>
into   (/Document/CstmrDrctDbtInitn/GrpHdr/InitgPty/Id/OrgId/Othr)[1]');
set @valor = @cifIntl;
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
  replace value of (/Document/CstmrDrctDbtInitn/GrpHdr/InitgPty/Id/OrgId/Othr/Id/text())[1]
  with     sql:variable("@valor")
')

-- Insertamos Nombre del esquema
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
insert <SchmeNm></SchmeNm>
into   (/Document/CstmrDrctDbtInitn/GrpHdr/InitgPty/Id/OrgId/Othr)[1]');

-- Insertamos Código
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
insert <Cd>Codigo</Cd>
into   (/Document/CstmrDrctDbtInitn/GrpHdr/InitgPty/Id/OrgId/Othr/SchmeNm)[1]');
set @valor = rtrim(@codigo);
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
  replace value of (/Document/CstmrDrctDbtInitn/GrpHdr/InitgPty/Id/OrgId/Othr/SchmeNm/Cd/text())[1]
  with     sql:variable("@valor")
')



/*		
select @numOrden=[nº orden], @numCliente=Numero, @CONTACTO=contacto, @IMPORTE=-Importe, @cifCliente=[CIF/NIF]
from ExtractoCliente 
where Remesa = @remesa and TipoApunte = 3
*/

--- Aquí empiezan los bucles
-- Carlos 29/08/14: creamos una tabla temporal, que será la que recorramos
if exists (select * from tempdb..sysobjects where id = object_id('tempdb..#remesa') ) drop table [dbo].[#remesa]
CREATE TABLE [#remesa] 
	(orden int not null,
	 numero varchar(10) not null,
	 contacto varchar(3) not null,
	 importe money not null default(0),
	 cif varchar(20),
	 bic varchar(11),
	 iban varchar(24),
	 ccc varchar(3),
	 documento varchar(10),
	 efecto varchar(3),
	 fechaVencimiento date,
	 fechaMandato date,
	 secuencia varchar(4),
	 fechaCobroEfectiva date)
             ON [PRIMARY]

insert into #remesa
	select e.[nº orden], e.Numero, e.contacto, -e.Importe, e.[CIF/NIF],
            rtrim(c.bic), c.pais+c.dc_iban+c.entidad+c.oficina+c.dc+c.[nº cuenta],
            e.CCC, e.[Nº Documento], e.Efecto, e.FechaVto, c.FechaMandato, c.Secuencia,
            case when e.FechaVto > @fechaCobro then cast(e.FechaVto as date)
                 else cast(@fechaCobro as date) end
	from ExtractoCliente as e inner join ccc as c
	on e.empresa = c.empresa and e.numero = c.cliente and e.contacto = c.contacto and e.ccc = c.numero
	where e.Remesa = @remesa and e.TipoApunte = 3 -- pago

declare curPmtInf cursor local fast_forward for 
   select secuencia, fechaCobroEfectiva from #remesa
   group by secuencia, fechaCobroEfectiva
   order by secuencia, fechaCobroEfectiva

open curPmtInf
fetch next from curPmtInf into @secuencia, @fechaCobroGrupo
while @@fetch_status = 0 begin


-- Insertamos Información del pago
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
insert <PmtInf>
	<PmtInfId>IdentificacionDeLaInformacionDelPago</PmtInfId>
	<PmtMtd>DD</PmtMtd>
	<NbOfTxs>Operaciones</NbOfTxs>
	<CtrlSum>ControlDeSuma</CtrlSum>
	<PmtTpInf>
		<SvcLvl>
            <Cd>SEPA</Cd>
        </SvcLvl>
        <LclInstrm>
            <Cd>Codigo</Cd>
        </LclInstrm>
        <SeqTp>RCUR</SeqTp>
        <CtgyPurp>
            <Cd>TRAD</Cd>
        </CtgyPurp>
    </PmtTpInf>
	<ReqdColltnDt>FechaDeCobro</ReqdColltnDt>
	<Cdtr>
		<Nm>Nombre</Nm>
	</Cdtr>
	<CdtrAcct>
		<Id>
			<IBAN>IBAN</IBAN>
		</Id>
	</CdtrAcct>
	<CdtrAgt>
		<FinInstnId>
			<BIC>BIC</BIC>
		</FinInstnId>
	</CdtrAgt>
	<ChrgBr>SLEV</ChrgBr>
	<CdtrSchmeId>
        <Id>
            <PrvtId>
                <Othr>
                    <Id>CIFINTL</Id>
                    <SchmeNm>
                        <Prtry>SEPA</Prtry>
                    </SchmeNm>
                </Othr>
            </PrvtId>
        </Id>
    </CdtrSchmeId>
</PmtInf>
into   (/Document/CstmrDrctDbtInitn)[1]');

/*
-- Insertamos Identificación de la información del pago
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
insert <PmtInfId>IdentificacionDeLaInformacionDelPago</PmtInfId>
into   (/Document/CstmrDrctDbtInitn/PmtInf[last()])[1]');
*/
set @valor = left(rtrim(@cif)+RTRIM(@remesa)+RTRIM(@secuencia)
                +convert(varchar(8), @fechaCobroGrupo, 112), 35);
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
  replace value of (/Document/CstmrDrctDbtInitn/PmtInf[last()]/PmtInfId/text())[1]
  with     sql:variable("@valor")
')
/*
-- Insertamos método de pago 2.2
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
insert <PmtMtd>DD</PmtMtd>
into   (/Document/CstmrDrctDbtInitn/PmtInf[last()])[1]');
*/
/*
-- Insertamos número de operaciones
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
insert <NbOfTxs>Operaciones</NbOfTxs>
into   (/Document/CstmrDrctDbtInitn/PmtInf[last()])[1]');
*/
set @valor = (select isnull(count(*),0) from #remesa as r
              where secuencia = @secuencia and fechaCobroEfectiva = @fechaCobroGrupo);
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
  replace value of (/Document/CstmrDrctDbtInitn/PmtInf[last()]/NbOfTxs/text())[1]
  with     sql:variable("@valor")
')
/*
-- Insertamos control de suma
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
insert <CtrlSum>ControlDeSuma</CtrlSum>
into   (/Document/CstmrDrctDbtInitn/PmtInf[last()])[1]');
*/
set @valor = (select isnull(sum(importe),0) from #remesa as r
              where secuencia = @secuencia and fechaCobroEfectiva = @fechaCobroGrupo);
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
  replace value of (/Document/CstmrDrctDbtInitn/PmtInf[last()]/CtrlSum/text())[1]
  with     sql:variable("@valor")
')
/*
-- Insertamos <SvcLvl>
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
insert         <PmtTpInf>
            <SvcLvl>
                <Cd>SEPA</Cd>
            </SvcLvl>
            <LclInstrm>
                <Cd>Codigo</Cd>
            </LclInstrm>
            <SeqTp>RCUR</SeqTp>
            <CtgyPurp>
                <Cd>TRAD</Cd>
            </CtgyPurp>
        </PmtTpInf>
into   (/Document/CstmrDrctDbtInitn/PmtInf[last()])[1]');
*/

-- Carlos 28/08/14: ponemos FRST si no tiene ningún cobro después del 01/07/14 en esa cuenta
-- no ponemos contacto en la select, porque el mandato se construye sin contacto
set @valor=@secuencia
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
	replace value of (/Document/CstmrDrctDbtInitn/PmtInf[last()]/PmtTpInf/SeqTp/text())[1]
	with     sql:variable("@valor")
')



set @valor=rtrim(@codigo)
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
  replace value of (/Document/CstmrDrctDbtInitn/PmtInf[last()]/PmtTpInf/LclInstrm/Cd/text())[1]
  with     sql:variable("@valor")
')

/*
-- Insertamos Fecha de cobro 2.18
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
insert <ReqdColltnDt>FechaDeCobro</ReqdColltnDt>
into   (/Document/CstmrDrctDbtInitn/PmtInf[last()])[1]');
*/
set @valor = (SELECT CONVERT(VARCHAR(10), @fechaCobroGrupo, 120) AS [YYYY-MM-DD])
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
  replace value of (/Document/CstmrDrctDbtInitn/PmtInf[last()]/ReqdColltnDt/text())[1]
  with     sql:variable("@valor")
')
/*
-- Insertamos acreedor 2.19
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
insert <Cdtr></Cdtr>
into   (/Document/CstmrDrctDbtInitn/PmtInf[last()])[1]');
*/
/*
-- Insertamos nombre 2.19
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
insert <Nm>Nombre</Nm>
into   (/Document/CstmrDrctDbtInitn/PmtInf[last()]/Cdtr)[1]');
*/
set @valor = left(@nombre,70);
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
  replace value of (/Document/CstmrDrctDbtInitn/PmtInf[last()]/Cdtr/Nm/text())[1]
  with     sql:variable("@valor")
')
/*
-- Insertamos dirección
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
insert <PstlAdr><Ctry>ES</Ctry></PstlAdr>
into   (/Document/CstmrDrctDbtInitn/PmtInf[last()]/Cdtr)[1]');
*/
/*
-- Insertamos cuenta del acreedor 2.20
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
insert <CdtrAcct></CdtrAcct>
into   (/Document/CstmrDrctDbtInitn/PmtInf[last()])[1]');
*/
/*
-- Insertamos identificación 2.20
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
insert <Id></Id>
into   (/Document/CstmrDrctDbtInitn/PmtInf[last()]/CdtrAcct)[1]');

-- Insertamos IBAN 2.20
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
insert <IBAN>IBAN</IBAN>
into   (/Document/CstmrDrctDbtInitn/PmtInf[last()]/CdtrAcct/Id)[1]');
*/
set @valor = left(@ibanEmpresa,70);
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
  replace value of (/Document/CstmrDrctDbtInitn/PmtInf[last()]/CdtrAcct/Id/IBAN/text())[1]
  with     sql:variable("@valor")
')
/*
-- Insertamos Entidad del acreedor 2.21
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
insert <CdtrAgt></CdtrAgt>
into   (/Document/CstmrDrctDbtInitn/PmtInf[last()])[1]');
*/
/*
-- Insertamos Identificación de la entidad 2.21
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
insert <FinInstnId></FinInstnId>
into   (/Document/CstmrDrctDbtInitn/PmtInf[last()]/CdtrAgt)[1]');
*/
/*
-- Insertamos BIC 2.21
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
insert <BIC>BIC</BIC>
into   (/Document/CstmrDrctDbtInitn/PmtInf[last()]/CdtrAgt/FinInstnId)[1]');
*/
set @valor = left(@bicEmpresa,11);
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
  replace value of (/Document/CstmrDrctDbtInitn/PmtInf[last()]/CdtrAgt/FinInstnId/BIC/text())[1]
  with     sql:variable("@valor")
')
/*
-- Insertamos Cláusula de Gastos 2.24
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
insert <ChrgBr>SLEV</ChrgBr>
into   (/Document/CstmrDrctDbtInitn/PmtInf[last()])[1]');
*/
/*
-- Insertamos schema id
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
insert                 <CdtrSchmeId>
                <Id>
                    <PrvtId>
                        <Othr>
                            <Id>CIFINTL</Id>
                            <SchmeNm>
                                <Prtry>SEPA</Prtry>
                            </SchmeNm>
                        </Othr>
                    </PrvtId>
                </Id>
            </CdtrSchmeId>
into   (/Document/CstmrDrctDbtInitn/PmtInf[last()])[1]');
*/
-- Modificamos el CIF internacional
set @valor = left(@cifIntl,20);
SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
  replace value of (/Document/CstmrDrctDbtInitn/PmtInf[last()]/CdtrSchmeId/Id/PrvtId/Othr/Id/text())[1]
  with     sql:variable("@valor")
')

	
declare curLineas cursor local fast_forward for 
select orden, numero, contacto, importe, cif, bic, upper(iban), ccc, documento, efecto, fechaMandato, fechaVencimiento
	from #remesa 
	where secuencia = @secuencia and fechaCobroEfectiva = @fechaCobroGrupo
	order by numero

open curLineas
fetch next from curLineas into @numOrden, @numCliente, @CONTACTO, @IMPORTE, @cifCliente, @bicCliente, @ibanCliente, @cccCliente, @numDocumento, @numEfecto, @fechaMandato, @fechaVto
while @@fetch_status = 0 begin

---------------------------------------------------------------------
--------------------- LINEAS ----------------------------------------
---------------------------------------------------------------------
	-- Comprobamos que tengamos los datos
	if @ibanCliente is null begin
		/*
		set @bicCliente = @bicEmpresa
		set @ibanCliente = @ibanEmpresa
		*/

		set @numCliente = rtrim(@numCliente)
		--select 'El cliente ' + @numCliente + ' no tiene un IBAN correcto. No se puede crear el fichero de la remesa.'
		raiserror('El cliente %s no tiene un IBAN correcto. No se puede crear el fichero de la remesa.',11,1, @numCliente)
		return -- sale del procedimiento con error

	end

	-- Cogemos el nombre del cliente
	select @nombreCliente = nombre from Clientes where empresa = @empresa and [Nº Cliente] = @numCliente and Contacto = @contacto
	select @nombreCliente = replace(@nombreCliente, '&', 'Y');
	select @nombreCliente = replace(@nombreCliente, '<', '&lt');
	select @nombreCliente = replace(@nombreCliente, '>', '&gt');
	select @nombreCliente = replace(@nombreCliente, '"', '&quot');
	select @nombreCliente = replace(@nombreCliente, '''', '&apos');
	select @nombreCliente = UPPER(@nombreCliente);
	select @nombreCliente = replace(@nombreCliente, 'Á', 'A');
	select @nombreCliente = replace(@nombreCliente, 'É', 'E');
	select @nombreCliente = replace(@nombreCliente, 'Í', 'I');
	select @nombreCliente = replace(@nombreCliente, 'Ó', 'O');
	select @nombreCliente = replace(@nombreCliente, 'Ú', 'U');
	select @nombreCliente = replace(@nombreCliente, '`', '&apos;');
	select @nombreCliente = replace(@nombreCliente, '´', '&apos;');

	if len(rtrim(ltrim(@nombreCliente))) <= 1 begin
		raiserror('El cliente %s no tiene nombre en alguno de sus contactos. No se puede crear el fichero de la remesa.',11,1, @numCliente)
		return -- sale del procedimiento con error
	end


	-- Insertamos Información de la operación de adeudo directo 2.28
	SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
	insert <DrctDbtTxInf>
		<PmtId>
			<EndToEndId>IdentificacionDeExtremoAExtremo</EndToEndId>
		</PmtId>
		<InstdAmt Ccy="EUR">ImporteOrdenado</InstdAmt>
		<DrctDbtTx>
            <MndtRltdInf>
                <MndtId>CLIENTE</MndtId>
                <DtOfSgntr>2009-10-31</DtOfSgntr>
            </MndtRltdInf>
        </DrctDbtTx>
		<DbtrAgt>
			<FinInstnId></FinInstnId>
		</DbtrAgt>
		<Dbtr>
            <Nm>NOMBRE</Nm>
        </Dbtr>
		<DbtrAcct>
			<Id>
				<IBAN>IBAN</IBAN>
			</Id>
		</DbtrAcct>
		<Purp>
			<Cd>GDSV</Cd>
		</Purp>
		<RmtInf>
			<Ustrd>CONCEPTO</Ustrd>
		</RmtInf>
	</DrctDbtTxInf>
	into   (/Document/CstmrDrctDbtInitn/PmtInf[last()])[1]');
	/*
	-- Insertamos Identificación del pago 2.29
	SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
	insert <PmtId></PmtId>
	into   (/Document/CstmrDrctDbtInitn/PmtInf[last()]/DrctDbtTxInf[last()])[1]');
	*/
	/*
	-- Insertamos Identificación de extremo a extremo 2.31
	SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
	insert <EndToEndId>IdentificacionDeExtremoAExtremo</EndToEndId>
	into   (/Document/CstmrDrctDbtInitn/PmtInf[last()]/DrctDbtTxInf[last()]/PmtId)[1]');
	*/
	
	set @id_movimiento = left(RTRIM(@cif) + '/'+ RTRIM(@numCliente) +'/'+ RTRIM(@CONTACTO) +'/'+ RTRIM(cast(@numOrden as CHAR)),35);
	set @valor = @id_movimiento;
	SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
	  replace value of (/Document/CstmrDrctDbtInitn/PmtInf[last()]/DrctDbtTxInf[last()]/PmtId/EndToEndId/text())[1]
	  with     sql:variable("@valor")
	')
	/*
	-- Insertamos Importe ordenado 2.44
	SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
	insert <InstdAmt Ccy="EUR">ImporteOrdenado</InstdAmt>
	into   (/Document/CstmrDrctDbtInitn/PmtInf[last()]/DrctDbtTxInf[last()])[1]');
	*/
	set @valor = left(@IMPORTE,12);
	SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
	  replace value of (/Document/CstmrDrctDbtInitn/PmtInf[last()]/DrctDbtTxInf[last()]/InstdAmt/text())[1]
	  with     sql:variable("@valor")
	')
	/*
	-- insertamos el DrctDbtTx
	SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
	insert <DrctDbtTx>
                    <MndtRltdInf>
                        <MndtId>CLIENTE</MndtId>
                        <DtOfSgntr>2009-10-31</DtOfSgntr>
                    </MndtRltdInf>
                </DrctDbtTx>
	into   (/Document/CstmrDrctDbtInitn/PmtInf[last()]/DrctDbtTxInf[last()])[1]');
	*/
	set @valor = left(rtrim(@empresa) + '/' + RTRIM(@numCliente)+'/'+rtrim(@cccCliente),35);
	SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
	  replace value of (/Document/CstmrDrctDbtInitn/PmtInf[last()]/DrctDbtTxInf[last()]/DrctDbtTx/MndtRltdInf/MndtId/text())[1]
	  with     sql:variable("@valor")
	')

	if @fechaMandato is not null begin
		set @valor=(SELECT CONVERT(VARCHAR(10), @fechaMandato, 120) AS [YYYY-MM-DD])
		SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
		replace value of (/Document/CstmrDrctDbtInitn/PmtInf[last()]/DrctDbtTxInf[last()]/DrctDbtTx/MndtRltdInf/DtOfSgntr/text())[1]
		with     sql:variable("@valor")
		')
	end
	/*
	-- Insertamos Entidad del deudor 2.70
	SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
	insert <DbtrAgt></DbtrAgt>
	into   (/Document/CstmrDrctDbtInitn/PmtInf[last()]/DrctDbtTxInf[last()])[1]');

	-- Insertamos Identificación de la entidad 2.70
	SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
	insert <FinInstnId></FinInstnId>
	into   (/Document/CstmrDrctDbtInitn/PmtInf[last()]/DrctDbtTxInf[last()]/DbtrAgt)[1]');
	*/
	-- Insertamos BIC 2.70 (Carlos: 15/07/14: Ahora es opcional)
	if @bicCliente is not null and rtrim(@bicCliente) <> '' begin
		SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
		insert <BIC>BIC</BIC>
		into   (/Document/CstmrDrctDbtInitn/PmtInf[last()]/DrctDbtTxInf[last()]/DbtrAgt/FinInstnId)[1]');
		set @valor = left(@bicCliente,11);
		SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
		  replace value of (/Document/CstmrDrctDbtInitn/PmtInf[last()]/DrctDbtTxInf[last()]/DbtrAgt/FinInstnId/BIC/text())[1]
		  with     sql:variable("@valor")
		')
	end
	/*
	-- Insertamos Deudor 2.72
	SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
	insert <Dbtr>
            <Nm>NOMBRE</Nm>
        </Dbtr>
	into   (/Document/CstmrDrctDbtInitn/PmtInf[last()]/DrctDbtTxInf[last()])[1]');
	*/
	set @valor = left(rtrim(@nombreCliente),70);
	SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
	  replace value of (/Document/CstmrDrctDbtInitn/PmtInf[last()]/DrctDbtTxInf[last()]/Dbtr/Nm/text())[1]
	  with     sql:variable("@valor")
	')

	/*
	-- Si el CIF no empieza por número es persona jurídica
	if CHARINDEX(LEFT(@cifCliente,1),'0123456789')=0 begin
		-- Insertamos Persona jurídica 2.72
		SET @valor = N'<OrgId></OrgId>';
		set @valorxml = convert (xml, @valor);
		SET @myDoc.modify('           
		insert sql:variable("@valorxml")
		into   (/Document/CstmrDrctDbtInitn/PmtInf/Dbtr)[1]');
	end else begin
		select top 1 * from Clientes
	end
	*/
	/*
	-- Insertamos Cuenta del deudor 2.73
	SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
	insert <DbtrAcct></DbtrAcct>
	into   (/Document/CstmrDrctDbtInitn/PmtInf[last()]/DrctDbtTxInf[last()])[1]');
	
	-- Insertamos Identificación 2.73
	SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
	insert <Id></Id>
	into   (/Document/CstmrDrctDbtInitn/PmtInf[last()]/DrctDbtTxInf[last()]/DbtrAcct)[1]');
	
	-- Insertamos IBAN 2.73
	SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
	insert <IBAN>IBAN</IBAN>
	into   (/Document/CstmrDrctDbtInitn/PmtInf[last()]/DrctDbtTxInf[last()]/DbtrAcct/Id)[1]');
	*/
	set @valor = left(@ibanCliente,70);
	SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
	  replace value of (/Document/CstmrDrctDbtInitn/PmtInf[last()]/DrctDbtTxInf[last()]/DbtrAcct/Id/IBAN/text())[1]
	  with     sql:variable("@valor")
	')
	/*
	-- Insertamos Propósito 2.76
	SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
	insert <Purp><Cd>GDSV</Cd></Purp>
	into   (/Document/CstmrDrctDbtInitn/PmtInf[last()]/DrctDbtTxInf[last()])[1]');
	
	-- Insertamos RmtInf
	SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
	insert 	<RmtInf>
				<Ustrd>CONCEPTO</Ustrd>
			</RmtInf>
	into   (/Document/CstmrDrctDbtInitn/PmtInf[last()]/DrctDbtTxInf[last()])[1]');
	*/
	set @valor = left('Pago Factura '+rtrim(@numDocumento),70) + ' Efecto '+ rtrim(@numEfecto) + ' Vto. ' + rtrim(convert(char,@fechaVto, 3));
	SET @myDoc.modify('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02"; 
	  replace value of (/Document/CstmrDrctDbtInitn/PmtInf[last()]/DrctDbtTxInf[last()]/RmtInf/Ustrd/text())[1]
	  with     sql:variable("@valor")
	')
	
	/*
	SELECT CAST( 
	CAST (('<?xml version="1.0" encoding="iso8859-1"?>'+ cast(@myDoc as varchar(max))) AS VARBINARY (MAX)) 
	 AS XML)
	*/
	fetch next from curLineas into @numOrden, @numCliente, @CONTACTO, @IMPORTE, @cifCliente, @bicCliente, @ibanCliente, @cccCliente, @numDocumento, @numEfecto, @fechaMandato, @fechaVto
end -- fetch del cursor curLineas
close curLineas
deallocate curLineas;

fetch next from curPmtInf into @secuencia, @fechaCobroGrupo
end -- fetch del cursor curPmtInf
close curPmtInf
deallocate curPmtInf;

-- Actualizamos los FRST a RCUR
-- Importante: no ponemos el contacto, porque éste no entra en la codificación de la referencia. Es decir, es el mismo mandato para todos los contactos.
update ccc set secuencia = 'RCUR'
from ExtractoCliente as e inner join ccc as c
on e.Empresa = c.Empresa and e.Número = c.Cliente and e.CCC = c.Número
where e.TipoApunte = 3 and Remesa = @remesa and e.ccc is not null and c.secuencia = 'FRST';

WITH XMLNAMESPACES (default 'urn:iso:std:iso:20022:tech:xsd:pain.008.001.02') 
SELECT @myDoc for xml path('');

--EXEC xp_cmdshell 'bcp "WITH XMLNAMESPACES (default 'urn:iso:std:iso:20022:tech:xsd:pain.008.001.02') SELECT @myDoc for xml path('')" queryout "C:\table.xml" -c -T'


--SELECT @myDoc for xml path('Document'), ELEMENTS XSINIL;

--SELECT @myDoc.query('declare default element namespace "urn:iso:std:iso:20022:tech:xsd:pain.008.001.02";*') for xml path('Document'), ELEMENTS XSINIL;

END

