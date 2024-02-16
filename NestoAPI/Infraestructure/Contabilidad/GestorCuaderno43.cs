using NestoAPI.Models.ApuntesBanco;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Contabilidad
{
    internal static class GestorCuaderno43
    {

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
                registroConcepto.Concepto += linea.Substring(42, 38);
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

        private static string ObtenerTextoConceptoComun(string codigoConcepto)
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


        private static DateTime ParsearFecha(string fecha)
        {
            // Asumiendo un formato específico de fecha en el cuaderno 43, ajustar según sea necesario
            int anio = Convert.ToInt32(fecha.Substring(0, 2));
            int mes = Convert.ToInt32(fecha.Substring(2, 2));
            int dia = Convert.ToInt32(fecha.Substring(4, 2));

            // Puedes ajustar el formato y la cultura según tus necesidades
            return new DateTime(2000 + anio, mes, dia);
        }
    }
}