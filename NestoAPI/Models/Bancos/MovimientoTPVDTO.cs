using System;
using System.Linq.Expressions;

namespace NestoAPI.Models.Bancos
{
    public class MovimientoTPVDTO
    {
        // Estructura del fichero
        private const int TipoRegistroStart = 1;
        private const int TipoRegistroLength = 2;

        private const int ModoCapturaStart = 2;
        private const int ModoCapturaLength = 1;

        private const int SesionStart = 3;
        private const int SesionLength = 3;

        private const int FechaCapturaStart = 6;
        private const int FechaCapturaLength = 6;

        private const int TerminalStart = 12;
        private const int TerminalLength = 11;

        private const int FechaOperacionStart = 23;
        private const int FechaOperacionLength = 6;

        private const int HoraOperacionStart = 29;
        private const int HoraOperacionLength = 4;

        private const int TipoOperacionStart = 33;
        private const int TipoOperacionLength = 3;

        private const int NumAutorizaciónStart = 37;
        private const int NumAutorizaciónLength = 6;

        private const int PANStart = 43;
        private const int PANLength = 22;

        private const int ImporteOperacionStart = 64;
        private const int ImporteOperacionLength = 10;

        private const int DescuentoAplicadoStart = 75;
        private const int DescuentoAplicadoLength = 4;

        private const int TipoDescuentoStart = 79;
        private const int TipoDescuentoLength = 1;

        private const int ImporteDescuentoAplicadoStart = 79;
        private const int ImporteDescuentoAplicadoLength = 10;

        private const int OperaciónAbonadaStart = 90;
        private const int OperaciónAbonadaLength = 1;

        private const int ImporteAbonoStart = 90;
        private const int ImporteAbonoLength = 10;

        private const int CodigoMonedaStart = 100;
        private const int CodigoMonedaLength = 3;

        private const int ReferenciaOperaciónStart = 104;
        private const int ReferenciaOperaciónLength = 12;

        private const int CréditoDébitoStart = 116;
        private const int CréditoDébitoLength = 1;

        private const int PendienteLiquidarStart = 117;
        private const int PendienteLiquidarLength = 1;

        private const int ServicuentaCaixarapidaStart = 118;
        private const int ServicuentaCaixarapidaLength = 1;

        private const int ReservadoStart = 119;
        private const int ReservadoLength = 11;

        private const int RedStart = 130;
        private const int RedLength = 1;

        private const int Reservado2Start = 130;
        private const int Reservado2Length = 369;

        // Propiedades de la clase
        //public string TipoRegistro { get; set; }
        public string ModoCaptura { get; set; }
        public string TextoModoCaptura { get; set; }
        public string Sesion { get; set; }
        public DateTime FechaCaptura { get; set; }
        public string Terminal { get; set; }
        public DateTime FechaOperacion { get; set; }
        public string HoraOperacion { get; set; }
        public string TipoOperacion { get; set; }
        //public string NumAutorización { get; set; }
        //public string PAN { get; set; }
        public decimal ImporteOperacion { get; set; }
        //public int DescuentoAplicado { get; set; }
        //public string TipoDescuento { get; set; }
        public decimal ImporteComision { get; set; }
        //public string OperaciónAbonada { get; set; }
        public decimal ImporteAbono { get; set; }
        public string CodigoMoneda { get; set; }
        //public string ReferenciaOperación { get; set; }
        //public string CréditoDébito { get; set; }
        //public string PendienteLiquidar { get; set; }
        //public string ServicuentaCaixarapida { get; set; }
        public string Comentarios { get; set; }
        //public int Red { get; set; }
        //public string Reservado2 { get; set; }

        // Método para parsear una línea del archivo
        public string Usuario { get; set; }
        public DateTime FechaCreacion { get; set; }
        
        public static MovimientoTPVDTO ParseFromLine(string line)
        {
            try
            {
                var movimiento = new MovimientoTPVDTO
                {
                    //TipoRegistro = line.Substring(TipoRegistroStart, TipoRegistroLength),
                    ModoCaptura = line.Substring(ModoCapturaStart, ModoCapturaLength),
                    TextoModoCaptura = GetModoCapturaDescription(line.Substring(ModoCapturaStart, ModoCapturaLength)),
                    Sesion = line.Substring(SesionStart, SesionLength),
                    FechaCaptura = ParseExactDate(line.Substring(FechaCapturaStart, FechaCapturaLength)),
                    Terminal = line.Substring(TerminalStart, TerminalLength),
                    FechaOperacion = ParseExactDateTime(line.Substring(FechaOperacionStart, FechaOperacionLength),
                        line.Substring(HoraOperacionStart, HoraOperacionLength)),
                    TipoOperacion = line.Substring(TipoOperacionStart, TipoOperacionLength),
                    //NumAutorización = line.Substring(NumAutorizaciónStart, NumAutorizaciónLength),
                    //PAN = line.Substring(PANStart, PANLength),
                    ImporteOperacion = ParseDecimal(line.Substring(ImporteOperacionStart, ImporteOperacionLength)),
                    //DescuentoAplicado = ParseInt(line.Substring(DescuentoAplicadoStart, DescuentoAplicadoLength)),
                    //TipoDescuento = line.Substring(TipoDescuentoStart, TipoDescuentoLength),
                    ImporteComision = ParseDecimal(line.Substring(ImporteDescuentoAplicadoStart, ImporteDescuentoAplicadoLength)),
                    //OperaciónAbonada = line.Substring(OperaciónAbonadaStart, OperaciónAbonadaLength),
                    ImporteAbono = ParseDecimal(line.Substring(ImporteAbonoStart, ImporteAbonoLength)),
                    CodigoMoneda = line.Substring(CodigoMonedaStart, CodigoMonedaLength),
                    //ReferenciaOperación = line.Substring(ReferenciaOperaciónStart, ReferenciaOperaciónLength),
                    //CréditoDébito = line.Substring(CréditoDébitoStart, CréditoDébitoLength),
                    //PendienteLiquidar = line.Substring(PendienteLiquidarStart, PendienteLiquidarLength),
                    //ServicuentaCaixarapida = line.Substring(ServicuentaCaixarapidaStart, ServicuentaCaixarapidaLength),
                    //Reservado = line.Substring(ReservadoStart, ReservadoLength),
                    //Red = ParseInt(line.Substring(RedStart, RedLength)),
                    Comentarios = line.Substring(Reservado2Start, Reservado2Length)
                };

                if (movimiento.TipoOperacion != "110" && movimiento.TipoOperacion != "010")
                {
                    throw new Exception("Tipo de operación erróneo. Los valores permitidos son '010' y '110'.");
                }

                //if (movimiento.TipoOperacion == "110") // devolución
                //{
                //    movimiento.ImporteOperacion = -movimiento.ImporteOperacion;
                //    movimiento.ImporteComision = -movimiento.ImporteComision;
                //    movimiento.ImporteAbono = -movimiento.ImporteAbono;
                //}

                return movimiento;
            } 
            catch (Exception ex)
            {
                throw ex;
            }
            
        }

        // Métodos de ayuda para conversión de tipos
        private static DateTime ParseExactDate(string dateString)
        {
            return DateTime.ParseExact(dateString, "ddMMyy", null);
        }
        private static DateTime ParseExactDateTime(string fecha, string hora)
        {
            string fechaHora = $"{fecha} {hora}";
            return DateTime.ParseExact(fechaHora, "ddMMyy HHmm", null);
        }
        private static decimal ParseDecimal(string decimalString)
        {
            try
            {
                return decimal.Parse(decimalString) / 100;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al convertir '{decimalString}' a decimal. Detalles: {ex.Message}");
                throw; // Lanza la excepción original después de imprimir el mensaje de error
            }
        }

        public static string GetModoCapturaDescription(string modoCapturaCode)
        {
            switch (modoCapturaCode)
            {
                case "1":
                    return "Terminal ON";
                case "2":
                    return "Tpv – PC";
                case "3":
                    return "Terminal Web";
                case "4":
                    return "Terminal Price";
                case "5":
                    return "Captura Oficina";
                case "6":
                    return "Soporte Magnético";
                default:
                    return string.Empty;
            }
        }
    }
}