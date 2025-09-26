using System;
using System.Net;

namespace NestoAPI.Models
{
    public class EnvioAgenciaDTO
    {
        public EnvioAgenciaDTO() { }
        public EnvioAgenciaDTO(EnviosAgencia envio)
        {
            if (envio == null)
            {
                throw new Exception("No se puede crear un envío nuevo desde un parámetro nulo");
            }
            AgenciaId = envio.Agencia;
            AgenciaNombre = envio.AgenciasTransporte?.Nombre;
            AgenciaIdentificador = envio.AgenciasTransporte?.Identificador;
            Cliente = envio.Cliente;
            Pedido = (int)envio.Pedido;
            Estado = envio.Estado;
            Fecha = envio.Fecha;
            CodigoBarras = envio.CodigoBarras;
            CodigoPostal = envio.CodPostal;
        }
        public int AgenciaId { get; set; }
        public string AgenciaIdentificador { get; set; }
        public string AgenciaNombre { get; set; }
        public string Cliente { get; set; }
        public int Pedido { get; set; }
        public short Estado { get; set; }
        public DateTime Fecha { get; set; }
        public string CodigoBarras { get; set; }
        public string CodigoPostal { get; set; }
        public string EnlaceSeguimiento
        {
            get
            {
                string enlace;
                switch (AgenciaNombre)
                {
                    case "ASM":
                        enlace = !string.IsNullOrEmpty(CodigoBarras) && !string.IsNullOrEmpty(CodigoPostal) ? string.Format("https://mygls.gls-spain.es/e/{0}/{1}", CodigoBarras, CodigoPostal) : string.Empty;
                        break;
                    case "OnTime":
                        if (!string.IsNullOrEmpty(Pedido.ToString()))
                        {
                            string referencia = WebUtility.UrlEncode(Cliente.Trim() + "-" + Pedido.ToString());
                            enlace = string.Format("https://ontimegts.alertran.net/gts/pub/clielocserv.seam?cliente=02890107&referencia={0}", referencia);
                        }
                        else
                        {
                            enlace = string.Empty;
                        }
                        break;
                    case "Correos Express":
                        enlace = !string.IsNullOrEmpty(CodigoBarras) ? string.Format("https://s.correosexpress.com/c?n={0}", CodigoBarras) : string.Empty;
                        break;
                    case "Sending":
                        enlace = !string.IsNullOrEmpty(AgenciaIdentificador) && !string.IsNullOrEmpty(CodigoBarras) ? string.Format("https://info.sending.es/fgts/pub/locNumServ.seam?cliente={0}&localizador={1}", AgenciaIdentificador, CodigoBarras) : string.Empty;
                        break;
                    default:
                        enlace = "error, agencia no definida";
                        break;
                }
                return enlace;
            }
        }

    }
}