using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.PedidosVenta.Retencion;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    // ================================================================ contrato (NestoAPI#859)

    /// <summary>POST api/RepartoPropio/RecogidasImprevistas/Comprobar: la parada que lleva (EnvioParada) o el cliente y el contacto.</summary>
    public class ComprobarRecogidaImprevistaDTO
    {
        /// <summary>El Id del envío de la agencia 14 de la parada. Si viene, manda: el cliente y el contacto se sacan del envío.</summary>
        public int? EnvioParada { get; set; }
        /// <summary>«1» si no viene.</summary>
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        /// <summary>«0» si no viene.</summary>
        public string Contacto { get; set; }
    }

    public class CrearRecogidaImprevistaDTO : ComprobarRecogidaImprevistaDTO
    {
        /// <summary>Guid del aparato (idempotencia): repetir con la misma clave devuelve la misma nota sin crear otra.</summary>
        public string Clave { get; set; }
        /// <summary>Del aparato.</summary>
        public DateTime? FechaHora { get; set; }
        /// <summary>1-99.</summary>
        public int Bultos { get; set; }
        /// <summary>Qué es (obligatorio, máx. 500).</summary>
        public string Texto { get; set; }
        /// <summary>JPEG en base64, opcional.</summary>
        public string FotoBase64 { get; set; }
        /// <summary>Se lo lleva aunque el cliente tenga un envío de agencia con retorno en camino (aviso urgente a Almacén).</summary>
        public bool AceptaRetornoVivo { get; set; }
    }

    public class RetornoVivoDTO
    {
        public int Envio { get; set; }
        /// <summary>Nombre de la agencia.</summary>
        public string Agencia { get; set; }
        public int Pedido { get; set; }
        /// <summary>Texto legible («En camino», «Con incidencia»…).</summary>
        public string Estado { get; set; }
        public DateTime FechaEnvio { get; set; }
        public int Bultos { get; set; }
    }

    public class ComprobacionRecogidaImprevistaDTO
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string NombreCliente { get; set; }
        public string Direccion { get; set; }
        public string Poblacion { get; set; }
        public bool HayRetornoVivo { get; set; }
        public List<RetornoVivoDTO> RetornosVivos { get; set; } = new List<RetornoVivoDTO>();
        public string Mensaje { get; set; }
    }

    public class RecogidaImprevistaDTO
    {
        public int PedidoNota { get; set; }
        /// <summary>«NR930500»: lo que la PDA enseña como Code128 y se lee al llegar a Algete (vale para todos sus bultos).</summary>
        public string CodigoDocumento { get; set; }
        /// <summary>«NR930500-1» … (ya «Recogido» en la furgoneta del repartidor).</summary>
        public List<string> CodigosBultos { get; set; } = new List<string>();
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string NombreCliente { get; set; }
        public bool AvisoUrgenteEnviado { get; set; }
        public string Mensaje { get; set; }
    }

    public class ResultadoRecogidaImprevista<T>
    {
        public HttpStatusCode Codigo { get; set; } = HttpStatusCode.OK;
        public string Mensaje { get; set; }
        public T Contenido { get; set; }
        /// <summary>«RETORNO_VIVO» en el 409.</summary>
        public string CodigoError { get; set; }
        public List<RetornoVivoDTO> RetornosVivos { get; set; }

        public static ResultadoRecogidaImprevista<T> Bien(T contenido, HttpStatusCode codigo = HttpStatusCode.OK) =>
            new ResultadoRecogidaImprevista<T> { Contenido = contenido, Codigo = codigo };
        public static ResultadoRecogidaImprevista<T> Mal(HttpStatusCode codigo, string mensaje) =>
            new ResultadoRecogidaImprevista<T> { Codigo = codigo, Mensaje = mensaje };
    }

    // ================================================================ dependencias

    public interface IRepositorioRecogidasImprevistas
    {
        bool ScriptAplicado();
        Task<EnviosAgencia> LeerEnvio(int id);
        Task<FichaNotaCobro> LeerFicha(string empresa, string cliente, string contacto);
        /// <summary>Envíos de OTRA agencia (no la 14) del contacto, con retorno, tramitados o incidentados, desde esa fecha.</summary>
        Task<List<EnviosAgencia>> LeerRetornosCandidatos(string empresa, string cliente, string contacto, DateTime desde);
        Task<Dictionary<int, string>> NombresAgencias(IReadOnlyCollection<int> agencias);
        Task ApuntarHistoria(int envio, string observaciones, string usuario);
        Task<RecogidaImprevistaGuardada> LeerPorClave(Guid clave);
        Task<RecogidaImprevistaGuardada> Crear(FilaRecogidaSolicitada fila, Guid clave, short bultos, string texto, IReadOnlyList<string> lineas,
            FichaNotaCobro ficha, string usuario, string comentario, bool avisoUrgente, Func<int, IReadOnlyList<MovimientoBultoReparto>> movimientos);
        Task GuardarFoto(int id, string ruta);
    }

    /// <summary>Refresca el seguimiento de un envío en su agencia (con un límite corto). True si lo ha podido consultar.</summary>
    public interface IRefrescoSeguimientoEnvio
    {
        Task<bool> Refrescar(EnviosAgencia envio);
    }

    public interface IAvisosRecogidaImprevista
    {
        /// <summary>En segundo plano y sin lanzar nunca: campana a Almacén, urgente si hay retornos aceptados, y haCambiado.</summary>
        void Avisar(RecogidaImprevistaDTO recogida, string repartidor, IReadOnlyList<RetornoVivoDTO> retornosAceptados);
    }

    // ================================================================ reglas y textos

    public static class TextosRecogidaImprevista
    {
        public const string CODIGO_RETORNO_VIVO = "RETORNO_VIVO";
        /// <summary>Campana normal a Almacén: hay una recogida imprevista en camino.</summary>
        public const string TIPO_NOTIFICACION = "RecogidaImprevista";
        /// <summary>Urgente (NotificacionesUrgentes.TIPOS): se ha recogido con un envío de agencia con retorno en camino. Referencia = el envío.</summary>
        public const string TIPO_URGENTE = "RecogidaConRetornoEnCamino";
        public const int MAXIMO_TEXTO = 500;
        public const int MAXIMO_BULTOS = 99;
        /// <summary>Hasta dónde se miran los envíos con retorno (uno más viejo ya no está «en camino»).</summary>
        public const int DIAS_ATRAS_RETORNOS = 60;

        /// <summary>Estado vivo: tramitado (en camino) o incidentado. Entregado, devuelto o cerrado, no.</summary>
        public static bool EsVivo(short estado) =>
            estado == Constantes.Agencias.ESTADO_TRAMITADO || estado == Constantes.Agencias.ESTADO_INCIDENTADO;

        /// <summary>Con retorno: cualquier valor distinto de 0 («sin retorno» es 0 en todas las agencias; CTT 2 = recogida en origen).</summary>
        public static bool TieneRetorno(EnviosAgencia e) => e != null && e.Retorno != 0;

        public static bool EsRetornoVivo(EnviosAgencia e) =>
            e != null && e.Agencia != Constantes.Agencias.AGENCIA_REPARTO_PROPIO && TieneRetorno(e) && EsVivo(e.Estado);

        public static string EstadoLegible(short estado) =>
            estado == Constantes.Agencias.ESTADO_INCIDENTADO ? "Con incidencia" : estado == Constantes.Agencias.ESTADO_TRAMITADO ? "En camino" : estado.ToString(CultureInfo.InvariantCulture);

        public static string MensajeRetornoVivo(IReadOnlyList<RetornoVivoDTO> retornos)
        {
            RetornoVivoDTO r = retornos[0];
            string otros = retornos.Count > 1 ? $" (y {retornos.Count - 1} más)" : string.Empty;
            return $"Este cliente tiene un envío de {r.Agencia} con retorno en camino (envío {r.Envio}, pedido {r.Pedido}){otros}. " +
                "Si se lleva el paquete, la agencia no le entregará el pedido.";
        }

        public static string CuerpoUrgente(RecogidaImprevistaDTO recogida, RetornoVivoDTO retorno, string repartidor) =>
            $"El repartidor {repartidor} se ha llevado {recogida.CodigoDocumento} del cliente {recogida.Cliente}/{recogida.Contacto} " +
            $"({recogida.NombreCliente}), que tiene el envío {retorno.Envio} de {retorno.Agencia} (pedido {retorno.Pedido}) con retorno en camino. " +
            $"Llame a {retorno.Agencia}: si el cliente ya no tiene el paquete del retorno, no le entregarán el pedido.";

        public static string CuerpoCampana(RecogidaImprevistaDTO recogida, string repartidor) =>
            $"El repartidor {repartidor} trae una recogida imprevista del cliente {recogida.Cliente}/{recogida.Contacto} ({recogida.NombreCliente}): " +
            $"{recogida.CodigoDocumento}, {recogida.CodigosBultos.Count} bulto(s). Se recibe en Algete leyendo el código de la nota.";

        public static string ComentarioNota(string repartidor, string texto) =>
            $"Recogida imprevista del repartidor {repartidor} (NestoAPI#859): {texto}\r\nYA RECOGIDA: no va en el reparto. " +
            "Se cierra al leer en Algete el código de la nota.";
    }

    // ================================================================ servicio

    public class ServicioRecogidasImprevistas
    {
        public const string MENSAJE_FALTA_SCRIPT = "Las recogidas imprevistas aún no están disponibles: falta lanzar el script " +
            "Scripts/Issue859_RecogidasImprevistas.sql. Avise a Informática.";

        private readonly IRepositorioRecogidasImprevistas repositorio;
        private readonly IRefrescoSeguimientoEnvio refresco;
        private readonly IAvisosRecogidaImprevista avisos;
        private readonly IAlmacenFotosBultos fotos;
        private readonly Func<DateTime> ahora;

        public ServicioRecogidasImprevistas(IRepositorioRecogidasImprevistas repositorio, IRefrescoSeguimientoEnvio refresco,
            IAvisosRecogidaImprevista avisos, IAlmacenFotosBultos fotos = null, Func<DateTime> ahora = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.refresco = refresco;
            this.avisos = avisos;
            this.fotos = fotos;
            this.ahora = ahora ?? (() => DateTime.Now);
        }

        private class Destino
        {
            public string Empresa;
            public string Cliente;
            public string Contacto;
            public FichaNotaCobro Ficha;
        }

        private async Task<(Destino destino, HttpStatusCode codigo, string error)> Resolver(ComprobarRecogidaImprevistaDTO peticion)
        {
            string empresa = Limpiar(peticion?.Empresa) ?? Constantes.Empresas.EMPRESA_POR_DEFECTO;
            string cliente = Limpiar(peticion?.Cliente);
            string contacto = Limpiar(peticion?.Contacto);
            if (peticion?.EnvioParada.HasValue == true)
            {
                EnviosAgencia envio = await repositorio.LeerEnvio(peticion.EnvioParada.Value).ConfigureAwait(false);
                if (envio == null)
                {
                    return (null, HttpStatusCode.NotFound, $"No existe el envío {peticion.EnvioParada.Value} de la parada.");
                }
                empresa = Limpiar(envio.Empresa) ?? empresa;
                cliente = Limpiar(envio.Cliente);
                contacto = Limpiar(envio.Contacto);
            }
            else if (cliente == null)
            {
                return (null, HttpStatusCode.BadRequest, "Indique la parada (EnvioParada) o el cliente y el contacto.");
            }
            contacto = contacto ?? "0";
            FichaNotaCobro ficha = cliente == null ? null : await repositorio.LeerFicha(empresa, cliente, contacto).ConfigureAwait(false);
            if (ficha == null)
            {
                return (null, HttpStatusCode.NotFound, $"No existe el cliente {cliente}/{contacto}.");
            }
            return (new Destino { Empresa = empresa, Cliente = cliente, Contacto = contacto, Ficha = ficha }, HttpStatusCode.OK, null);
        }

        /// <summary>Los retornos vivos del contacto, refrescando antes su seguimiento. El mensaje dice si alguna agencia no ha contestado.</summary>
        private async Task<(List<RetornoVivoDTO> retornos, string aviso)> RetornosVivos(Destino destino)
        {
            List<EnviosAgencia> candidatos = (await repositorio.LeerRetornosCandidatos(destino.Empresa, destino.Cliente, destino.Contacto,
                ahora().Date.AddDays(-TextosRecogidaImprevista.DIAS_ATRAS_RETORNOS)).ConfigureAwait(false) ?? new List<EnviosAgencia>())
                .Where(TextosRecogidaImprevista.EsRetornoVivo).ToList();
            if (!candidatos.Any())
            {
                return (new List<RetornoVivoDTO>(), null);
            }
            Dictionary<int, string> nombres = await repositorio.NombresAgencias(candidatos.Select(c => c.Agencia).Distinct().ToList()).ConfigureAwait(false)
                ?? new Dictionary<int, string>();
            var sinContestar = new List<string>();
            foreach (EnviosAgencia envio in candidatos)
            {
                bool refrescado = false;
                try
                {
                    refrescado = refresco != null && await refresco.Refrescar(envio).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Recogida imprevista #859] No se ha podido refrescar el seguimiento del envío {envio.Numero}: {ex.Message}", ex));
                }
                if (!refrescado)
                {
                    sinContestar.Add(Nombre(nombres, envio.Agencia));
                }
            }
            List<RetornoVivoDTO> vivos = candidatos.Where(TextosRecogidaImprevista.EsRetornoVivo).Select(e => new RetornoVivoDTO
            {
                Envio = e.Numero,
                Agencia = Nombre(nombres, e.Agencia),
                Pedido = e.Pedido ?? 0,
                Estado = TextosRecogidaImprevista.EstadoLegible(e.Estado),
                FechaEnvio = e.Fecha,
                Bultos = e.Bultos
            }).OrderBy(r => r.FechaEnvio).ToList();
            string aviso = sinContestar.Any()
                ? $"No se ha podido consultar ahora a {string.Join(", ", sinContestar.Distinct())}: se usa el último estado conocido."
                : null;
            return (vivos, aviso);
        }

        public async Task<ResultadoRecogidaImprevista<ComprobacionRecogidaImprevistaDTO>> Comprobar(ComprobarRecogidaImprevistaDTO peticion)
        {
            if (!repositorio.ScriptAplicado())
            {
                return ResultadoRecogidaImprevista<ComprobacionRecogidaImprevistaDTO>.Mal(HttpStatusCode.ServiceUnavailable, MENSAJE_FALTA_SCRIPT);
            }
            (Destino destino, HttpStatusCode codigo, string error) = await Resolver(peticion).ConfigureAwait(false);
            if (destino == null)
            {
                return ResultadoRecogidaImprevista<ComprobacionRecogidaImprevistaDTO>.Mal(codigo, error);
            }
            (List<RetornoVivoDTO> retornos, string aviso) = await RetornosVivos(destino).ConfigureAwait(false);
            string mensaje = retornos.Any()
                ? TextosRecogidaImprevista.MensajeRetornoVivo(retornos)
                : "Este cliente no tiene envíos de agencia con retorno en camino: puede recogerlo.";
            return ResultadoRecogidaImprevista<ComprobacionRecogidaImprevistaDTO>.Bien(new ComprobacionRecogidaImprevistaDTO
            {
                Empresa = destino.Empresa,
                Cliente = destino.Cliente,
                Contacto = destino.Contacto,
                NombreCliente = Limpiar(destino.Ficha.Nombre),
                Direccion = Limpiar(destino.Ficha.Direccion),
                Poblacion = Limpiar(destino.Ficha.Poblacion),
                HayRetornoVivo = retornos.Any(),
                RetornosVivos = retornos,
                Mensaje = aviso == null ? mensaje : $"{mensaje} {aviso}"
            });
        }

        public async Task<ResultadoRecogidaImprevista<RecogidaImprevistaDTO>> Crear(CrearRecogidaImprevistaDTO peticion, string usuario)
        {
            if (!repositorio.ScriptAplicado())
            {
                return Mal(HttpStatusCode.ServiceUnavailable, MENSAJE_FALTA_SCRIPT);
            }
            if (peticion == null || !Guid.TryParse(peticion.Clave?.Trim(), out Guid clave) || clave == Guid.Empty)
            {
                return Mal(HttpStatusCode.BadRequest, "Falta la Clave de la recogida (un Guid del aparato).");
            }
            RecogidaImprevistaGuardada previa = await repositorio.LeerPorClave(clave).ConfigureAwait(false);
            if (previa != null)
            {
                return await Repetida(previa).ConfigureAwait(false);
            }
            if (peticion.Bultos < 1 || peticion.Bultos > TextosRecogidaImprevista.MAXIMO_BULTOS)
            {
                return Mal(HttpStatusCode.BadRequest, $"Los bultos tienen que estar entre 1 y {TextosRecogidaImprevista.MAXIMO_BULTOS}.");
            }
            string texto = peticion.Texto?.Trim();
            if (string.IsNullOrEmpty(texto))
            {
                return Mal(HttpStatusCode.BadRequest, "Escriba qué le han dado (por ejemplo, «2 cajas de devolución»).");
            }
            if (texto.Length > TextosRecogidaImprevista.MAXIMO_TEXTO)
            {
                return Mal(HttpStatusCode.BadRequest, $"El texto no puede pasar de {TextosRecogidaImprevista.MAXIMO_TEXTO} caracteres.");
            }
            byte[] foto = null;
            if (!string.IsNullOrWhiteSpace(peticion.FotoBase64))
            {
                try
                {
                    string limpia = peticion.FotoBase64.Trim();
                    int coma = limpia.IndexOf(',');
                    foto = Convert.FromBase64String(limpia.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && coma > 0 ? limpia.Substring(coma + 1) : limpia);
                }
                catch (FormatException)
                {
                    return Mal(HttpStatusCode.BadRequest, "La foto no es una imagen válida (base64).");
                }
            }
            (Destino destino, HttpStatusCode codigo, string error) = await Resolver(peticion).ConfigureAwait(false);
            if (destino == null)
            {
                return Mal(codigo, error);
            }
            (List<RetornoVivoDTO> retornos, _) = await RetornosVivos(destino).ConfigureAwait(false);
            if (retornos.Any() && !peticion.AceptaRetornoVivo)
            {
                return new ResultadoRecogidaImprevista<RecogidaImprevistaDTO>
                {
                    Codigo = HttpStatusCode.Conflict,
                    CodigoError = TextosRecogidaImprevista.CODIGO_RETORNO_VIVO,
                    Mensaje = TextosRecogidaImprevista.MensajeRetornoVivo(retornos),
                    RetornosVivos = retornos
                };
            }

            string repartidor = usuario?.Trim() ?? "NestoAPI";
            short bultos = (short)peticion.Bultos;
            DateTime momento = ahora();
            DateTime fechaHora = peticion.FechaHora ?? momento;
            string nombre = Limpiar(destino.Ficha.Nombre);
            var fila = new FilaRecogidaSolicitada
            {
                Empresa = destino.Empresa,
                Cliente = destino.Cliente,
                Contacto = destino.Contacto,
                Texto = texto,
                Bultos = bultos,
                QuienRecoge = QuienRecogeRecogida.REPARTO,
                Vendedor = Limpiar(destino.Ficha.Vendedor),
                UsuarioSolicita = ServicioRecogidasSolicitadas.Recortar(repartidor, 50),
                FechaSolicitud = momento,
                Estado = EstadosRecogidaSolicitada.CONFIRMADA
            };
            RecogidaImprevistaGuardada guardada = await repositorio.Crear(fila, clave, bultos, texto, ServicioRecogidasSolicitadas.LineasTexto(texto),
                destino.Ficha, repartidor, TextosRecogidaImprevista.ComentarioNota(repartidor, texto), retornos.Any(),
                numero => Movimientos(numero, bultos, clave, repartidor, fechaHora, momento, nombre)).ConfigureAwait(false);
            if (guardada == null)
            {
                return Mal(HttpStatusCode.Conflict, "No se ha podido crear la nota de recogida: vuelva a intentarlo.");
            }
            if (guardada.YaExistia)
            {
                return await Repetida(guardada).ConfigureAwait(false);
            }

            RecogidaImprevistaDTO dto = Dto(guardada, nombre);
            dto.AvisoUrgenteEnviado = retornos.Any();
            dto.Mensaje = $"Nota de recogida {dto.CodigoDocumento} creada y {bultos} bulto(s) en su furgoneta. Enséñela al llegar a Algete.";
            if (foto != null)
            {
                dto.Mensaje += await GuardarFoto(guardada, clave, foto).ConfigureAwait(false);
            }
            foreach (RetornoVivoDTO r in retornos)
            {
                try
                {
                    await repositorio.ApuntarHistoria(r.Envio, $"Recogida imprevista {dto.CodigoDocumento} del repartidor {repartidor} con este envío con retorno " +
                        "en camino: avisado Almacén para llamar a la agencia (NestoAPI#859).", repartidor).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Recogida imprevista #859] No se ha podido apuntar en la historia del envío {r.Envio}: {ex.Message}", ex));
                }
            }
            avisos?.Avisar(dto, repartidor, retornos);
            return ResultadoRecogidaImprevista<RecogidaImprevistaDTO>.Bien(dto, HttpStatusCode.Created);
        }

        internal static List<MovimientoBultoReparto> Movimientos(int pedido, short bultos, Guid clave, string repartidor, DateTime fechaHora, DateTime registro, string nombre)
            => ServicioRecogidasSolicitadas.CodigosBultos(pedido, bultos).Select((codigo, i) => new MovimientoBultoReparto
            {
                Codigo = codigo,
                TipoCodigo = CodigoBultoFurgoneta.NR,
                Pedido = pedido,
                Bulto = i + 1,
                Total = bultos,
                Tipo = TiposMovimientoBulto.RECOGIDO,
                Origen = OrigenesMovimientoBulto.REPARTIDOR,
                Usuario = repartidor,
                FechaHora = fechaHora,
                FechaRegistro = registro,
                Descripcion = $"Recogida imprevista {pedido}" + (string.IsNullOrWhiteSpace(nombre) ? string.Empty : $": {nombre}"),
                ClaveIdempotencia = NucleoFurgoneta.ClaveDerivada(clave, codigo, TiposMovimientoBulto.RECOGIDO)
            }).ToList();

        private async Task<string> GuardarFoto(RecogidaImprevistaGuardada guardada, Guid clave, byte[] foto)
        {
            if (fotos == null || !fotos.Configurado)
            {
                return " (La foto no se ha guardado: el almacenamiento de fotos no está configurado.)";
            }
            try
            {
                string ruta = $"recogidas-imprevistas/{guardada.PedidoNota}-{clave:N}.jpg";
                await fotos.Subir(ruta, foto, "image/jpeg").ConfigureAwait(false);
                await repositorio.GuardarFoto(guardada.Id, ruta).ConfigureAwait(false);
                return string.Empty;
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Recogida imprevista #859] No se ha podido guardar la foto de la nota {guardada.PedidoNota}: {ex.Message}", ex));
                return " (La foto no se ha podido guardar.)";
            }
        }

        private async Task<ResultadoRecogidaImprevista<RecogidaImprevistaDTO>> Repetida(RecogidaImprevistaGuardada previa)
        {
            FichaNotaCobro ficha = await repositorio.LeerFicha(previa.Empresa, previa.Cliente, previa.Contacto).ConfigureAwait(false);
            RecogidaImprevistaDTO dto = Dto(previa, Limpiar(ficha?.Nombre));
            dto.AvisoUrgenteEnviado = previa.AvisoUrgente;
            dto.Mensaje = $"Nota de recogida {dto.CodigoDocumento} creada y {dto.CodigosBultos.Count} bulto(s) en su furgoneta. Enséñela al llegar a Algete.";
            return ResultadoRecogidaImprevista<RecogidaImprevistaDTO>.Bien(dto, HttpStatusCode.Created);
        }

        private static RecogidaImprevistaDTO Dto(RecogidaImprevistaGuardada g, string nombre)
        {
            int pedido = g.PedidoNota ?? 0;
            return new RecogidaImprevistaDTO
            {
                PedidoNota = pedido,
                CodigoDocumento = CodigoNotaEntrega.Documento(true, pedido),
                CodigosBultos = ServicioRecogidasSolicitadas.CodigosBultos(pedido, g.Bultos),
                Empresa = Limpiar(g.Empresa),
                Cliente = Limpiar(g.Cliente),
                Contacto = Limpiar(g.Contacto),
                NombreCliente = nombre
            };
        }

        private static ResultadoRecogidaImprevista<RecogidaImprevistaDTO> Mal(HttpStatusCode codigo, string mensaje) =>
            ResultadoRecogidaImprevista<RecogidaImprevistaDTO>.Mal(codigo, mensaje);

        private static string Nombre(Dictionary<int, string> nombres, int agencia) =>
            nombres.TryGetValue(agencia, out string n) && !string.IsNullOrWhiteSpace(n) ? n.Trim() : $"la agencia {agencia}";

        private static string Limpiar(string valor)
        {
            string limpio = valor?.Trim();
            return string.IsNullOrEmpty(limpio) ? null : limpio;
        }
    }

    // ================================================================ producción

    public class RepositorioRecogidasImprevistasSql : IRepositorioRecogidasImprevistas
    {
        private readonly NVEntities db;
        private readonly RepositorioRecogidasSolicitadas recogidas;

        public RepositorioRecogidasImprevistasSql(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
            recogidas = new RepositorioRecogidasSolicitadas(db);
        }

        public bool ScriptAplicado() => recogidas.ScriptImprevistasAplicado() && ColumnasOpcionales.ExisteTabla(db, RepositorioFurgoneta.TABLA);

        public Task<EnviosAgencia> LeerEnvio(int id) => db.EnviosAgencias.FirstOrDefaultAsync(e => e.Numero == id);

        public Task<FichaNotaCobro> LeerFicha(string empresa, string cliente, string contacto) => recogidas.LeerFicha(empresa, cliente, contacto);

        public async Task<List<EnviosAgencia>> LeerRetornosCandidatos(string empresa, string cliente, string contacto, DateTime desde)
        {
            int propio = Constantes.Agencias.AGENCIA_REPARTO_PROPIO;
            short tramitado = Constantes.Agencias.ESTADO_TRAMITADO;
            short incidentado = Constantes.Agencias.ESTADO_INCIDENTADO;
            // Los char vienen con relleno: se filtra el contacto en memoria
            return (await db.EnviosAgencias
                .Where(e => e.Empresa == empresa && e.Cliente == cliente && e.Agencia != propio && e.Retorno != 0
                    && (e.Estado == tramitado || e.Estado == incidentado) && e.Fecha >= desde)
                .ToListAsync().ConfigureAwait(false))
                .Where(e => string.Equals(e.Contacto?.Trim(), contacto, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public async Task<Dictionary<int, string>> NombresAgencias(IReadOnlyCollection<int> agencias)
            => (await db.AgenciasTransportes.Where(a => agencias.Contains(a.Numero)).Select(a => new { a.Numero, a.Nombre })
                .ToListAsync().ConfigureAwait(false))
                .GroupBy(a => a.Numero).ToDictionary(g => g.Key, g => g.First().Nombre?.Trim());

        public async Task ApuntarHistoria(int envio, string observaciones, string usuario)
        {
            _ = db.EnviosHistorias.Add(new EnvioHistoria
            {
                NumeroEnvio = envio,
                Campo = "RecogidaImprevista",
                ValorAnterior = string.Empty,
                Observaciones = observaciones,
                Usuario = ServicioRecogidasSolicitadas.Recortar(usuario, 50),
                FechaModificacion = DateTime.Now
            });
            _ = await db.SaveChangesAsync().ConfigureAwait(false);
        }

        public Task<RecogidaImprevistaGuardada> LeerPorClave(Guid clave) => recogidas.LeerImprevistaPorClave(clave);

        public Task<RecogidaImprevistaGuardada> Crear(FilaRecogidaSolicitada fila, Guid clave, short bultos, string texto, IReadOnlyList<string> lineas,
            FichaNotaCobro ficha, string usuario, string comentario, bool avisoUrgente, Func<int, IReadOnlyList<MovimientoBultoReparto>> movimientos)
            => recogidas.CrearImprevista(fila, clave, bultos, texto, lineas, ficha, usuario, comentario, avisoUrgente, movimientos);

        public Task GuardarFoto(int id, string ruta) => recogidas.GuardarFotoImprevista(id, ruta);
    }

    /// <summary>El «actualizar seguimiento» a demanda de siempre (la agencia y AplicarSeguimiento), con un límite corto de tiempo.</summary>
    public class RefrescoSeguimientoEnvio : IRefrescoSeguimientoEnvio
    {
        public static readonly TimeSpan LIMITE = TimeSpan.FromSeconds(4);

        private readonly NVEntities db;
        private readonly IFabricaAgenciasRemotas fabrica;

        public RefrescoSeguimientoEnvio(NVEntities db, IFabricaAgenciasRemotas fabrica)
        {
            this.db = db;
            this.fabrica = fabrica;
        }

        public async Task<bool> Refrescar(EnviosAgencia envio)
        {
            if (envio == null || string.IsNullOrWhiteSpace(envio.CodigoBarras))
            {
                return false;
            }
            ISeguimientoAgenciaRemota agencia = fabrica?.CrearSeguimiento(envio.Agencia);
            if (agencia == null)
            {
                return false;
            }
            Task<SeguimientoEnvioRemoto> consulta = agencia.ConsultarSeguimientoAsync(envio.CodigoBarras.Trim());
            if (await Task.WhenAny(consulta, Task.Delay(LIMITE)).ConfigureAwait(false) != consulta)
            {
                return false;
            }
            SeguimientoEnvioRemoto seguimiento;
            try
            {
                seguimiento = await consulta.ConfigureAwait(false);
            }
            catch (Exception)
            {
                return false;
            }
            if (SeguimientoEnviosJobsService.AplicarSeguimiento(envio, seguimiento))
            {
                _ = await db.SaveChangesAsync().ConfigureAwait(false);
            }
            return true;
        }
    }

    /// <summary>Los avisos: campana a Almacén, urgente con retorno aceptado y haCambiado. En segundo plano; nunca lanza.</summary>
    public class AvisosRecogidaImprevista : IAvisosRecogidaImprevista
    {
        private readonly Func<NotificacionPushDTO, Task> avisarAlmacen;
        private readonly Action<string, string> haCambiado;
        private readonly Action<Func<Task>> lanzar;

        public AvisosRecogidaImprevista()
            : this(n => new AvisadorRetencionAlmacen().Avisar(n), (grupo, usuario) => new AvisosAlmacenSignalR().HaCambiado(grupo, usuario, null, null),
                  trabajo => Task.Run(trabajo))
        {
        }

        internal AvisosRecogidaImprevista(Func<NotificacionPushDTO, Task> avisarAlmacen, Action<string, string> haCambiado, Action<Func<Task>> lanzar)
        {
            this.avisarAlmacen = avisarAlmacen;
            this.haCambiado = haCambiado;
            this.lanzar = lanzar ?? (t => t());
        }

        public void Avisar(RecogidaImprevistaDTO recogida, string repartidor, IReadOnlyList<RetornoVivoDTO> retornosAceptados)
        {
            List<NotificacionPushDTO> notificaciones = Componer(recogida, repartidor, retornosAceptados);
            lanzar(async () =>
            {
                foreach (NotificacionPushDTO n in notificaciones)
                {
                    try
                    {
                        await avisarAlmacen(n).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        ElmahHelper.Log(new Exception($"[Recogida imprevista #859] No se ha podido avisar a Almacén ({n.Tipo}) de {recogida?.CodigoDocumento}: {ex.Message}", ex));
                    }
                }
                try
                {
                    haCambiado?.Invoke(AvisosAlmacen.GRUPO_REPARTO, repartidor);
                    haCambiado?.Invoke(AvisosAlmacen.GRUPO_RECOGIDAS_SOLICITADAS, repartidor);
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Recogida imprevista #859] No se ha podido avisar al AlmacenHub: {ex.Message}", ex));
                }
            });
        }

        /// <summary>La campana normal y, por cada retorno aceptado, la urgente (referencia = el envío de la agencia).</summary>
        internal static List<NotificacionPushDTO> Componer(RecogidaImprevistaDTO recogida, string repartidor, IReadOnlyList<RetornoVivoDTO> retornosAceptados)
        {
            var lista = new List<NotificacionPushDTO>
            {
                new NotificacionPushDTO
                {
                    Titulo = $"Recogida imprevista en camino: {recogida.CodigoDocumento}",
                    Cuerpo = TextosRecogidaImprevista.CuerpoCampana(recogida, repartidor),
                    Tipo = TextosRecogidaImprevista.TIPO_NOTIFICACION,
                    Datos = new Dictionary<string, string>
                    {
                        ["tipo"] = TextosRecogidaImprevista.TIPO_NOTIFICACION,
                        ["empresa"] = recogida.Empresa,
                        ["pedido"] = recogida.PedidoNota.ToString(CultureInfo.InvariantCulture),
                        ["codigo"] = recogida.CodigoDocumento
                    }
                }
            };
            foreach (RetornoVivoDTO r in retornosAceptados ?? new List<RetornoVivoDTO>())
            {
                lista.Add(new NotificacionPushDTO
                {
                    Titulo = $"Llame a {r.Agencia}: el repartidor se ha llevado la devolución de un cliente con retorno en camino",
                    Cuerpo = TextosRecogidaImprevista.CuerpoUrgente(recogida, r, repartidor),
                    Tipo = TextosRecogidaImprevista.TIPO_URGENTE,
                    Datos = new Dictionary<string, string>
                    {
                        [NotificacionesUrgentes.DATO_TIPO] = TextosRecogidaImprevista.TIPO_URGENTE,
                        [NotificacionesUrgentes.DATO_REFERENCIA] = r.Envio.ToString(CultureInfo.InvariantCulture),
                        ["envio"] = r.Envio.ToString(CultureInfo.InvariantCulture),
                        ["agencia"] = r.Agencia,
                        ["pedidoEnvio"] = r.Pedido.ToString(CultureInfo.InvariantCulture),
                        ["empresa"] = recogida.Empresa,
                        ["pedido"] = recogida.PedidoNota.ToString(CultureInfo.InvariantCulture),
                        ["codigo"] = recogida.CodigoDocumento
                    }
                });
            }
            return lista;
        }
    }

    /// <summary>
    /// NestoAPI#859: la urgente «recogida con retorno en camino» sigue pendiente mientras el envío de la agencia siga vivo (en camino o con
    /// incidencia). Cuando la agencia lo entrega, lo devuelve o alguien lo cierra, deja de sonar.
    /// </summary>
    public class ComprobadorRecogidasConRetornoEnCamino : IComprobadorUrgentesPendientes
    {
        private readonly Func<IReadOnlyCollection<int>, Task<Dictionary<int, short>>> leerEstados;

        public ComprobadorRecogidasConRetornoEnCamino(Func<IReadOnlyCollection<int>, Task<Dictionary<int, short>>> leerEstados)
        {
            this.leerEstados = leerEstados ?? throw new ArgumentNullException(nameof(leerEstados));
        }

        public static ComprobadorRecogidasConRetornoEnCamino DeLaBaseDeDatos(NVEntities db) =>
            new ComprobadorRecogidasConRetornoEnCamino(async ids => (await db.EnviosAgencias.Where(e => ids.Contains(e.Numero))
                .Select(e => new { e.Numero, e.Estado }).ToListAsync().ConfigureAwait(false)).ToDictionary(e => e.Numero, e => e.Estado));

        public string Tipo => TextosRecogidaImprevista.TIPO_URGENTE;

        public async Task<ISet<string>> Pendientes(IReadOnlyCollection<string> referencias)
        {
            var pendientes = new HashSet<string>(StringComparer.Ordinal);
            List<int> ids = (referencias ?? new List<string>())
                .Select(r => int.TryParse(r, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) ? id : 0).Where(id => id > 0).Distinct().ToList();
            if (!ids.Any())
            {
                return pendientes;
            }
            Dictionary<int, short> estados = await leerEstados(ids).ConfigureAwait(false) ?? new Dictionary<int, short>();
            foreach (KeyValuePair<int, short> e in estados.Where(e => TextosRecogidaImprevista.EsVivo(e.Value)))
            {
                _ = pendientes.Add(e.Key.ToString(CultureInfo.InvariantCulture));
            }
            return pendientes;
        }
    }
}
