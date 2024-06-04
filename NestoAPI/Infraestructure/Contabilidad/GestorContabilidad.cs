using NestoAPI.Models;
using NestoAPI.Models.ApuntesBanco;
using NestoAPI.Models.Bancos;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Infraestructure.Contabilidad
{
    public class GestorContabilidad
    {
        private readonly IContabilidadService _servicio;

        public GestorContabilidad(IContabilidadService servicio)
        {
            this._servicio = servicio;
        }
        public async Task<int> CrearLineasDiario(List<PreContabilidad> lineas)
        {
            return await _servicio.CrearLineas(lineas);
        }

        public async Task<int> CrearLineasDiarioYContabilizar(List<PreContabilidad> lineas)
        {
            // test: si hay varios diarios en lineas hay que dar error
            // lo mismo si hay varias empresas

            return await _servicio.CrearLineasYContabilizarDiario(lineas);
        }

        public static async Task<ContenidoCuaderno43> LeerCuaderno43(string contenido)
        {
            ContenidoCuaderno43 cuaderno43 = new ContenidoCuaderno43();
            ApunteBancarioDTO apunteBancarioActual = null;

            using (StringReader reader = new StringReader(contenido))
            {
                string linea;
                while ((linea = await reader.ReadLineAsync()) != null)
                {
                    string codigoRegistro = linea.Substring(0, 2);

                    switch (codigoRegistro)
                    {
                        case "11":
                            cuaderno43.Cabecera = AsignarValoresRegistroCabecera(linea);
                            break;

                        case "22":
                            apunteBancarioActual = AsignarValoresRegistroPrincipalMovimientos(linea, reader);
                            cuaderno43.Apuntes.Add(apunteBancarioActual);
                            break;

                        case "23":
                            // Es un registro complementario de concepto, asignamos los valores al apunte bancario actual
                            if (apunteBancarioActual != null)
                            {
                                ConceptoComplementario registroConcepto = AsignarValoresRegistroComplementarioConcepto(linea);
                                apunteBancarioActual.RegistrosConcepto.Add(registroConcepto);
                            }
                            break;

                        case "24":
                            // Es un registro complementario de equivalencia de importe, asignamos los valores al apunte bancario actual
                            if (apunteBancarioActual != null)
                            {
                                apunteBancarioActual.ImporteEquivalencia = AsignarValoresRegistroComplementarioEquivalencia(linea);
                            }
                            break;

                        case "33":
                            // Es un registro final de la cuenta, asignamos los valores a un nuevo objeto RegistroFinalCuenta
                            cuaderno43.FinalCuenta = AsignarValoresRegistroFinalCuenta(linea);
                            break;

                        case "88":
                            // Es un registro final de fichero, asignamos los valores a un nuevo objeto RegistroFinalFichero
                            cuaderno43.FinalFichero = AsignarValoresRegistroFinFichero(linea);
                            break;


                        default:
                            // Otros códigos de registro, no hacemos nada por ahora
                            break;
                    }
                }
            }

            return cuaderno43;
        }

        private static RegistroCabeceraCuenta AsignarValoresRegistroCabecera(string linea)
        {
            RegistroCabeceraCuenta registroCabecera = new RegistroCabeceraCuenta();
            registroCabecera.CodigoRegistroCabecera = linea.Substring(0, 2);
            registroCabecera.ClaveEntidad = linea.Substring(2, 4);
            registroCabecera.ClaveOficina = linea.Substring(6, 4);
            registroCabecera.NumeroCuenta = linea.Substring(10, 10);
            registroCabecera.FechaInicial = ParsearFecha(linea.Substring(20, 6)); // Asumiendo un formato específico de fecha
            registroCabecera.FechaFinal = ParsearFecha(linea.Substring(26, 6));   // Asumiendo un formato específico de fecha
            registroCabecera.ClaveDebeOHaber = linea.Substring(32, 1);
            registroCabecera.ImporteSaldoInicial = Convert.ToDecimal(linea.Substring(33, 14)) / 100; // Ajustar según formato y posición del punto decimal
            registroCabecera.ClaveDivisa = linea.Substring(47, 3);
            registroCabecera.ModalidadInformacion = linea.Substring(50, 1);
            registroCabecera.NombreAbreviado = linea.Substring(51, 26).Trim(); // Eliminar espacios en blanco al final
            registroCabecera.CampoLibreCabecera = linea.Substring(77, 3);

            return registroCabecera;
        }

        private static ApunteBancarioDTO AsignarValoresRegistroPrincipalMovimientos(string linea, StringReader reader)
        {
            ApunteBancarioDTO apunteBancario = new ApunteBancarioDTO();
            apunteBancario.CodigoRegistroPrincipal = linea.Substring(0, 2);
            apunteBancario.ClaveOficinaOrigen = linea.Substring(6, 4);
            apunteBancario.FechaOperacion = ParsearFecha(linea.Substring(10, 6)); // Asumiendo un formato específico de fecha
            apunteBancario.FechaValor = ParsearFecha(linea.Substring(16, 6));   // Asumiendo un formato específico de fecha
            apunteBancario.ConceptoComun = linea.Substring(22, 2);
            apunteBancario.TextoConceptoComun = ObtenerTextoConceptoComun(apunteBancario.ConceptoComun);
            apunteBancario.ConceptoPropio = linea.Substring(24, 3);
            apunteBancario.ClaveDebeOHaberMovimiento = linea.Substring(27, 1);
            apunteBancario.ImporteMovimiento = Convert.ToDecimal(linea.Substring(28, 14)) / 100; // Ajustar según formato y posición del punto decimal
            apunteBancario.NumeroDocumento = linea.Substring(42, 10);
            apunteBancario.Referencia1 = linea.Substring(52, 12);
            apunteBancario.Referencia2 = linea.Substring(64, 16);

            // Registros Complementarios de Concepto (Hasta un máximo de 5)
            apunteBancario.RegistrosConcepto = new List<ConceptoComplementario>();


            // Registro Complementario de Información de Equivalencia de Importe (Opcional)
            apunteBancario.ImporteEquivalencia = new EquivalenciaDivisas();

            return apunteBancario;
        }

        private static ConceptoComplementario AsignarValoresRegistroComplementarioConcepto(string linea)
        {
            ConceptoComplementario registroConcepto = new ConceptoComplementario();
            registroConcepto.CodigoRegistroConcepto = linea.Substring(0, 2);
            registroConcepto.CodigoDatoConcepto = linea.Substring(2, 2);
            registroConcepto.Concepto = linea.Substring(4, 38);

            // Verificamos si hay un segundo campo de concepto
            if (linea.Length > 42)
            {
                // Asumiendo que los siguientes 38 caracteres son el segundo campo de concepto
                registroConcepto.Concepto2 = linea.Substring(42, 38);
            }

            return registroConcepto;
        }

        private static EquivalenciaDivisas AsignarValoresRegistroComplementarioEquivalencia(string linea)
        {
            EquivalenciaDivisas registroEquivalencia = new EquivalenciaDivisas();
            registroEquivalencia.CodigoRegistroEquivalencia = linea.Substring(0, 2);
            registroEquivalencia.CodigoDatoEquivalencia = linea.Substring(2, 2);
            registroEquivalencia.ClaveDivisaOrigen = linea.Substring(4, 3);
            registroEquivalencia.ImporteEquivalencia = Convert.ToDecimal(linea.Substring(7, 14)) / 100; // Ajustar según formato y posición del punto decimal
            registroEquivalencia.CampoLibreEquivalencia = linea.Substring(21, 59); // Ajustar según la longitud de tu campo libre

            return registroEquivalencia;
        }

        private static RegistroFinalCuenta AsignarValoresRegistroFinalCuenta(string linea)
        {
            RegistroFinalCuenta registroFinalCuenta = new RegistroFinalCuenta();
            registroFinalCuenta.CodigoRegistroFinal = linea.Substring(0, 2);
            registroFinalCuenta.ClaveEntidadFinal = linea.Substring(2, 4);
            registroFinalCuenta.ClaveOficinaFinal = linea.Substring(6, 4);
            registroFinalCuenta.NumeroCuentaFinal = linea.Substring(10, 10);
            registroFinalCuenta.NumeroApuntesDebe = Convert.ToInt32(linea.Substring(20, 5));
            registroFinalCuenta.TotalImportesDebe = Convert.ToDecimal(linea.Substring(25, 14)) / 100; // Ajustar según formato y posición del punto decimal
            registroFinalCuenta.NumeroApuntesHaber = Convert.ToInt32(linea.Substring(39, 5));
            registroFinalCuenta.TotalImportesHaber = Convert.ToDecimal(linea.Substring(44, 14)) / 100; // Ajustar según formato y posición del punto decimal
            registroFinalCuenta.CodigoSaldoFinal = linea.Substring(58, 1);
            registroFinalCuenta.SaldoFinal = Convert.ToDecimal(linea.Substring(59, 14)) / 100; // Ajustar según formato y posición del punto decimal
            registroFinalCuenta.ClaveDivisaFinal = linea.Substring(73, 3);
            registroFinalCuenta.CampoLibreFinal = linea.Substring(76, 4);

            return registroFinalCuenta;
        }

        private static RegistroFinalFichero AsignarValoresRegistroFinFichero(string linea)
        {
            RegistroFinalFichero registroFinFichero = new RegistroFinalFichero();
            registroFinFichero.CodigoRegistroFinFichero = linea.Substring(0, 2);
            registroFinFichero.Nueves = linea.Substring(2, 18);
            registroFinFichero.NumeroRegistros = Convert.ToInt32(linea.Substring(20, 6));
            registroFinFichero.CampoLibreFinFichero = linea.Substring(26, 54);

            return registroFinFichero;
        }


        private static DateTime ParsearFecha(string fecha)
        {
            // Asumiendo un formato específico de fecha en el cuaderno 43, ajustar según sea necesario
            int anio = Convert.ToInt32(fecha.Substring(0, 2));
            int mes = Convert.ToInt32(fecha.Substring(2, 2));
            int dia = Convert.ToInt32(fecha.Substring(4, 2));

            // Puedes ajustar el formato y la cultura según tus necesidades
            return new DateTime(2000 + anio, mes, dia);
        }

        public static string ObtenerTextoConceptoComun(string codigoConcepto)
        {
            switch (codigoConcepto)
            {
                case "01":
                    return "TALONES - REINTEGROS";
                case "02":
                    return "ABONARÉS - ENTREGAS - INGRESOS";
                case "03":
                    return "DOMICILIADOS - RECIBOS - LETRAS - PAGOS POR SU CTA.";
                case "04":
                    return "GIROS - TRANSFERENCIAS - TRASPASOS - CHEQUES";
                case "05":
                    return "AMORTIZACIONES PRÉSTAMOS, CRÉDITOS, ETC.";
                case "06":
                    return "REMESAS EFECTOS";
                case "07":
                    return "SUSCRIPCIONES - DIV. PASIVOS - CANJES.";
                case "08":
                    return "DIV. CUPONES - PRIMA JUNTA - AMORTIZACIONES";
                case "09":
                    return "OPERACIONES DE BOLSA Y/O COMPRA /VENTA VALORES";
                case "10":
                    return "CHEQUES GASOLINA";
                case "11":
                    return "CAJERO AUTOMÁTICO";
                case "12":
                    return "TARJETAS DE CRÉDITO - TARJETAS DÉBITO";
                case "13":
                    return "OPERACIONES EXTRANJERO";
                case "14":
                    return "DEVOLUCIONES E IMPAGADOS";
                case "15":
                    return "NÓMINAS - SEGUROS SOCIALES";
                case "16":
                    return "TIMBRES - CORRETAJE - PÓLIZA";
                case "17":
                    return "INTERESES - COMISIONES – CUSTODIA - GASTOS E IMPUESTOS";
                case "98":
                    return "ANULACIONES - CORRECCIONES ASIENTO";
                case "99":
                    return "VARIOS";
                default:
                    return "DESCONOCIDO";
            }
        }

        public async Task<bool> PersistirCuaderno43(ContenidoCuaderno43 apuntes)
        {
            return await _servicio.PersistirCuaderno43(apuntes);
        }

        internal static async Task<int> PuntearApuntes(int? apunteBancoId, int? apunteContabilidadId, decimal importePunteo, string simboloPunteo, int? grupoPunteo, string usuario)
        {
            using (var db = new NVEntities())
            {
                decimal importeBanco = 0;
                Models.Contabilidad movimientoContabilidad;
                decimal importeContabilidad = 0;
                decimal importeYaPunteadoBanco;
                decimal importeYaPunteadoContabilidad;
                if (grupoPunteo == null || apunteBancoId != null)
                {
                    importeBanco = (await db.ApuntesBancarios.SingleAsync(a => a.Id == apunteBancoId)).ImporteMovimiento;
                }
                if (grupoPunteo == null || apunteContabilidadId != null)
                {
                    movimientoContabilidad = await db.Contabilidades.SingleAsync(c => c.Nº_Orden == apunteContabilidadId);
                    importeContabilidad = movimientoContabilidad.Debe - movimientoContabilidad.Haber;
                }

                /*
                // Ajustar los importes restando el importe ya punteado
                importeYaPunteadoBanco = ObtenerImportePunteoBanco(apunteBancoId, db);
                importeYaPunteadoContabilidad = ObtenerImportePunteoContabilidad(apunteContabilidadId, db);

                importeBanco -= importeYaPunteadoBanco;
                importeContabilidad -= importeYaPunteadoContabilidad;
                
                var importePunteo = importeBanco > importeContabilidad ? importeContabilidad : importeBanco;

                if (importePunteo == 0)
                {
                    throw new Exception("No se puede puntear porque alguno de los movimientos ya está completamente punteado");
                }
                */

                var punteo = new ConciliacionBancariaPunteo
                {
                    ApunteBancoId = apunteBancoId,
                    ApunteContabilidadId = apunteContabilidadId,
                    ImportePunteado = importePunteo,
                    SimboloPunteo = simboloPunteo,
                    GrupoPunteo = grupoPunteo,
                    Usuario = usuario,
                    FechaCreacion = DateTime.Now
                };

                db.ConciliacionesBancariasPunteos.Add(punteo);
                await db.SaveChangesAsync();
                return punteo.Id;
            }
        }

        public async Task<bool> PuntearPorImporte(string empresa, string cuenta, decimal importe)
        {
            return await _servicio.PuntearPorImporte(empresa, cuenta, importe);
        }

        private static decimal ObtenerImportePunteoBanco(int? apunteId, NVEntities db)
        {
            // Obtener la suma de importes punteados en las liquidaciones anteriores
            var importePunteadoAnterior = db.ConciliacionesBancariasPunteos
                .Where(p => p.ApunteBancoId == apunteId)
                .Select(p => p.ImportePunteado)
                .DefaultIfEmpty(0)
                .Sum();

            return importePunteadoAnterior;
        }

        private static decimal ObtenerImportePunteoContabilidad(int? apunteId, NVEntities db)
        {
            // Obtener la suma de importes punteados en las liquidaciones anteriores
            var importePunteadoAnterior = db.ConciliacionesBancariasPunteos
                .Where(p => p.ApunteContabilidadId == apunteId)
                .Select(p => p.ImportePunteado)
                .DefaultIfEmpty(0)
                .Sum();

            return importePunteadoAnterior;
        }

        internal static List<MovimientoTPVDTO> LeerMovimientosTPV(string contenido, string usuario)
        {
            List<MovimientoTPVDTO> movimientos = new List<MovimientoTPVDTO>();

            // Suponiendo que 'contenido' es un string con varias líneas
            string[] lineasDelArchivo = contenido.Split(new[] { Environment.NewLine }, StringSplitOptions.None);

            foreach (var line in lineasDelArchivo)
            {
                if (line.StartsWith("20"))
                {
                    MovimientoTPVDTO movimiento = MovimientoTPVDTO.ParseFromLine(line);
                    movimiento.Usuario = usuario;
                    movimientos.Add(movimiento);
                }
            }
            return movimientos;
        }

        internal async Task<bool> PersistirMovimientosTPV(List<MovimientoTPVDTO> movimientosTPV)
        {
            return await _servicio.PersistirMovimientosTPV(movimientosTPV);
        }

        internal async Task ContabilizarComisionesTarjetas(List<MovimientoTPVDTO> movimientosTPV)
        {
            await _servicio.ContabilizarComisionesTarjetas(movimientosTPV);
        }
    }
}