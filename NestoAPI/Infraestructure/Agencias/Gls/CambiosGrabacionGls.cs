using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Agencias.Gls
{
    /// <summary>
    /// NestoAPI#552 (fase 5): las agencias de GLS que tramita la API: la 1 (Nueva Visión) y la 5 (la de la empresa espejo,
    /// con su propia cuenta: AgenciasTransporte.Identificador y PrefijoCodigoBarras de la fila 5, igual que AgenciaASM).
    /// </summary>
    public static class AgenciasGls
    {
        public static bool Es(int agencia)
            => agencia == Constantes.Agencias.AGENCIA_GLS || agencia == Constantes.Agencias.AGENCIA_GLS_EMPRESA_ESPEJO;
    }

    /// <summary>NestoAPI#552 (fase 5): un dato del envío que ha cambiado desde que se grabó en GLS.</summary>
    public class CambioGrabacionGls
    {
        /// <summary>Ruta dentro de Servicios, p. ej. «Envio/Destinatario/CP».</summary>
        public string Campo { get; set; }
        /// <summary>Para el usuario, p. ej. «Código postal».</summary>
        public string Nombre { get; set; }
        /// <summary>Lo que tiene GLS (lo que se grabó).</summary>
        public string Antes { get; set; }
        /// <summary>Lo que tiene ahora el envío (lo que se acaba de mandar).</summary>
        public string Ahora { get; set; }

        public override string ToString() => $"{Nombre}: «{Antes}» → «{Ahora}»";
    }

    /// <summary>
    /// NestoAPI#552 (fase 5, decisión de Carlos del 27/10/26): cuando GLS contesta -33 («ya existe el código de barras»), el
    /// envío se da por tramitado, pero si sus datos han cambiado desde que se grabó, GLS tiene los viejos: se avisa en el
    /// resumen de «Tramitar todos» para que Almacén lo revise con GLS. Comparador puro: la grabación que GLS aceptó (el
    /// GrabaServicios con éxito guardado en AgenciasLlamadasWeb) contra la petición que se acaba de mandar.
    /// </summary>
    public static class CambiosGrabacionGls
    {
        /// <summary>
        /// Lo que no cuenta: la fecha (al tramitar se manda la del día, así que siempre cambia) y el remitente (son nuestros
        /// datos, no los del paquete).
        /// </summary>
        private static bool SeIgnora(string campo)
            => campo == ComparadorPeticionesGls.CAMPO_FECHA
            || campo.StartsWith("Envio/Remite/", StringComparison.Ordinal);

        /// <summary>
        /// Los datos que han cambiado entre la grabación que tiene GLS (<paramref name="grabado"/>) y la petición de ahora
        /// (<paramref name="actual"/>), los dos nodos Servicios. Vacía si coinciden.
        /// </summary>
        public static List<CambioGrabacionGls> Detectar(XElement grabado, XElement actual)
        {
            if (grabado == null) throw new ArgumentNullException(nameof(grabado));
            if (actual == null) throw new ArgumentNullException(nameof(actual));

            return ComparadorPeticionesGls.Comparar(grabado, actual)
                .Where(d => !SeIgnora(d.Campo))
                // Los campos char de la BD pueden llegar con o sin relleno según quién montó la petición.
                .Where(d => !string.Equals(d.Nesto?.Trim(), d.Api?.Trim(), StringComparison.Ordinal))
                .Select(d => new CambioGrabacionGls
                {
                    Campo = d.Campo,
                    Nombre = NombreCampo(d.Campo),
                    Antes = d.Nesto?.Trim(),
                    Ahora = d.Api?.Trim()
                })
                .ToList();
        }

        /// <summary>El nombre que lee el usuario.</summary>
        public static string NombreCampo(string campo)
        {
            switch (campo)
            {
                case "@uidcliente": return "Cuenta de GLS";
                case "Envio/Servicio": return "Servicio";
                case "Envio/Horario": return "Horario";
                case "Envio/Bultos": return "Bultos";
                case "Envio/Retorno": return "Retorno";
                case "Envio/Destinatario/Nombre": return "Nombre";
                case "Envio/Destinatario/Direccion": return "Dirección";
                case "Envio/Destinatario/Poblacion": return "Población";
                case "Envio/Destinatario/Provincia": return "Provincia";
                case "Envio/Destinatario/Pais": return "País";
                case "Envio/Destinatario/CP": return "Código postal";
                case "Envio/Destinatario/Telefono": return "Teléfono";
                case "Envio/Destinatario/Movil": return "Móvil";
                case "Envio/Destinatario/Email": return "Correo";
                case "Envio/Destinatario/Observaciones": return "Observaciones";
                case "Envio/Destinatario/ATT": return "Atención";
                case "Envio/Referencias/Referencia": return "Referencia";
                case "Envio/Importes/Reembolso": return "Reembolso";
                default: return campo;
            }
        }

        /// <summary>El aviso para el resumen de «Tramitar todos» (de usted). Null si no hay nada que revisar.</summary>
        public static string Aviso(int? pedido, IReadOnlyCollection<CambioGrabacionGls> cambios)
        {
            if (cambios == null || cambios.Count == 0)
            {
                return null;
            }
            return $"GLS ya tenía grabado el envío del pedido {pedido}, pero con otros datos: " +
                string.Join("; ", cambios.Select(c => c.ToString())) +
                ". Se ha dado por tramitado: revíselo con GLS para que lo entreguen con los datos de ahora.";
        }

        /// <summary>Cuando no se encuentra la grabación anterior y no se puede saber si los datos han cambiado.</summary>
        public static string AvisoSinGrabacionAnterior(int? pedido)
            => $"GLS ya tenía grabado el envío del pedido {pedido}, pero no se encuentra la primera grabación para comprobar " +
               "si los datos han cambiado. Se ha dado por tramitado: revíselo con GLS.";

        /// <summary>
        /// De las llamadas guardadas (las más recientes primero), el nodo Servicios de la última grabación que GLS ACEPTÓ
        /// (return="0") para ese código de barras, o null. Una -33 no cuenta: GLS se quedó con lo de antes.
        /// </summary>
        public static XElement GrabacionAceptada(IEnumerable<(string Peticion, string Respuesta)> llamadas, string codigoBarras)
        {
            string codigo = codigoBarras?.Trim();
            if (string.IsNullOrEmpty(codigo) || llamadas == null)
            {
                return null;
            }
            foreach ((string peticion, string respuesta) in llamadas)
            {
                XElement servicios = PeticionGls.ServiciosDeLaPeticion(peticion);
                string delEnvio = servicios?.Elements(PeticionGls.Ns + "Envio").FirstOrDefault()?.Attribute("codbarras")?.Value?.Trim();
                if (delEnvio != codigo)
                {
                    continue;
                }
                if (RespuestaGrabaServiciosGls.Interpretar(respuesta).Exito)
                {
                    return servicios;
                }
            }
            return null;
        }

        /// <summary>
        /// La grabación aceptada de un envío buscándola en AgenciasLlamadasWeb: llamadas de GLS («ASM») con éxito, desde el día
        /// en que se creó el envío y con su código de barras en la petición. Solo lee.
        /// </summary>
        public static Func<EnviosAgencia, Task<XElement>> DesdeLaBD(NVEntities db)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            return async envio =>
            {
                string codigo = envio?.CodigoBarras?.Trim();
                if (string.IsNullOrEmpty(codigo))
                {
                    return null;
                }
                string marca = "codbarras=\"" + codigo;
                DateTime desde = envio.Fecha.Date;
                var llamadas = await db.AgenciasLlamadasWeb.AsNoTracking()
                    .Where(l => l.Agencia == SombraPeticionesGls.AGENCIA_LLAMADAS && l.Exito && l.Fecha >= desde && l.CuerpoLlamada.Contains(marca))
                    .OrderByDescending(l => l.Id)
                    .Take(10)
                    .Select(l => new { l.CuerpoLlamada, l.CuerpoRespuesta })
                    .ToListAsync().ConfigureAwait(false);
                return GrabacionAceptada(llamadas.Select(l => (l.CuerpoLlamada, l.CuerpoRespuesta)), codigo);
            };
        }
    }
}
