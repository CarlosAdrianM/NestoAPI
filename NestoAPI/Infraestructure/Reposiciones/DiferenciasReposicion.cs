using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Reposiciones
{
    // ------------------------------------------------------------------------------------------------------------------
    // NestoAPI#634 (corte 1): diferencias entre lo enviado y lo recibido en una reposición (caso 38631 / 80915: Andre preparó
    // 1 ud. que no fue en la caja, Paloma recibió 0 y Algete se quedó con −1 sin que nadie lo supiera).
    //
    // Decisiones (Carlos, 14/10/26):
    //  - Constancia: cada diferencia queda en DiferenciasReposiciones con estado (Pendiente / Resuelta, con motivo y quién).
    //  - Falta (recibido de menos): la diferencia vuelve sola al stock del origen al terminar la recepción; sobra (recibido de
    //    más): se descuenta del origen. Por prdExtrProducto (única puerta a ExtractoProducto) en el diario «_ErrRepo»; en
    //    Algete, la entrada queda «pendiente de ubicar» y la salida se quita del hueco por la puerta única de Ubicaciones.
    //    Detrás del interruptor Reposiciones:AjustarStockDiferencias (apagado: solo constancia y aviso).
    //  - Comprobación física en el origen (Almacén en Algete; cada tienda en la suya), que la resuelve con motivo.
    //  - Aviso al grupo Almacén + a quien la preparó (AvisadorReposiciones).
    // ------------------------------------------------------------------------------------------------------------------

    public static class EstadosDiferenciaReposicion
    {
        public const string PENDIENTE = "Pendiente";
        public const string RESUELTA = "Resuelta";
    }

    public static class ResolucionesDiferenciaReposicion
    {
        /// <summary>El origen la ha comprobado y cuadra con lo que dejó el ajuste.</summary>
        public const string CONFIRMADA = "Confirmada";
        /// <summary>Falta: lo que «volvió» al origen no está. Baja.</summary>
        public const string NO_ESTABA = "NoEstaba";
        /// <summary>Sobra: lo que se descontó del origen sigue allí. Alta.</summary>
        public const string SOBRABA = "Sobraba";
        /// <summary>La tienda se equivocó al recibir: se deshace el ajuste del origen y se corrige el destino.</summary>
        public const string ERROR_RECEPCION = "ErrorRecepcion";

        public static readonly string[] TODAS = { CONFIRMADA, NO_ESTABA, SOBRABA, ERROR_RECEPCION };

        /// <summary>Las que tienen sentido para una diferencia: «NoEstaba» solo en una falta y «Sobraba» solo en una sobra.</summary>
        public static List<string> Posibles(int diferencia)
        {
            return diferencia < 0
                ? new List<string> { CONFIRMADA, NO_ESTABA, ERROR_RECEPCION }
                : new List<string> { CONFIRMADA, SOBRABA, ERROR_RECEPCION };
        }

        /// <summary>La resolución bien escrita (sin distinguir mayúsculas). Null si no existe.</summary>
        public static string Normalizar(string resolucion)
        {
            string limpia = resolucion?.Trim();
            return TODAS.FirstOrDefault(r => string.Equals(r, limpia, StringComparison.OrdinalIgnoreCase));
        }
    }

    public static class AjustesDiferenciaReposicion
    {
        public const string HECHO = "Hecho";
        /// <summary>El interruptor estaba apagado al recibir: no se tocó el stock.</summary>
        public const string APAGADO = "Apagado";
        public const string FALLIDO = "Fallido";
    }

    /// <summary>
    /// INTERRUPTOR <c>Reposiciones:AjustarStockDiferencias</c> en Web.config. APAGADO por defecto (falta la clave o no dice
    /// "true"): se deja constancia de la diferencia y se avisa, pero no se toca el stock (ni al recibir ni al resolver). OJO:
    /// el valor del Web.config del repo es el que se publica.
    /// </summary>
    public static class InterruptorAjusteDiferencias
    {
        internal const string CLAVE = "Reposiciones:AjustarStockDiferencias";

        public static bool EstaActivo()
        {
            return string.Equals(ConfigurationManager.AppSettings[CLAVE]?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Una diferencia al terminar la recepción (lo que se inserta en DiferenciasReposiciones).</summary>
    public class DiferenciaReposicionNueva
    {
        public string Empresa { get; set; }
        public int Traspaso { get; set; }
        public string Origen { get; set; }
        public string Destino { get; set; }
        public string Producto { get; set; }
        public int Enviado { get; set; }
        public int Leido { get; set; }
        public int Diferencia => Leido - Enviado;
        public string UsuarioRecepcion { get; set; }
        public string Preparador { get; set; }
        public Guid IdRecepcion { get; set; }
    }

    /// <summary>Un movimiento de stock por una diferencia: positivo entra, negativo sale.</summary>
    public class MovimientoStockDiferencia
    {
        public string Empresa { get; set; }
        public string Almacen { get; set; }
        public string Producto { get; set; }
        public int Cantidad { get; set; }
        public int Traspaso { get; set; }
        public string Texto { get; set; }
        public string Usuario { get; set; }
        /// <summary>
        /// Al quitar de Ubicaciones, coger primero lo «pendiente de ubicar» (lo que puso el ajuste al recibir) y después
        /// el hueco del que salió el traspaso. Al recibir de más es al revés: lo que salió de más estaba en ese hueco.
        /// </summary>
        public bool PrimeroPendienteDeUbicar { get; set; }
    }

    public class ResultadoMovimientoStock
    {
        /// <summary>ExtractoProducto.[Nº Orden] del apunte.</summary>
        public int? NumeroOrdenExtracto { get; set; }
        /// <summary>Unidades de una salida que no se han podido quitar de ningún hueco (Ubicaciones se queda con ellas).</summary>
        public int UnidadesSinHueco { get; set; }
    }

    /// <summary>Lo que ha pasado con el ajuste del origen de una diferencia al terminar la recepción.</summary>
    public class ResultadoAjusteDiferencia
    {
        public int Id { get; set; }
        public string Producto { get; set; }
        public int Diferencia { get; set; }
        public string Origen { get; set; }
        public string Ajuste { get; set; }
        public string Mensaje { get; set; }
    }

    /// <summary>Lo que se hace con DiferenciasReposiciones y con el stock dentro de una transacción.</summary>
    public interface ITransaccionDiferenciasReposicion
    {
        /// <summary>La fila bloqueada hasta el final de la transacción. Null si no existe.</summary>
        Task<DiferenciaReposicionDTO> Leer(string empresa, int id);
        /// <summary>prdExtrProducto (diario «_ErrRepo») y, si el almacén lleva control de ubicaciones, la puerta de Ubicaciones.</summary>
        Task<ResultadoMovimientoStock> Mover(MovimientoStockDiferencia movimiento);
        Task GuardarAjuste(int id, string ajuste, int? numeroOrden, string mensaje);
        Task GuardarResolucion(int id, string resolucion, string usuario, string comentario, int? numeroOrdenOrigen, int? numeroOrdenDestino);
    }

    public interface IRepositorioDiferenciasReposicion
    {
        /// <summary>Todo o nada.</summary>
        Task<T> EnTransaccion<T>(Func<ITransaccionDiferenciasReposicion, Task<T>> trabajo);
        /// <param name="estado">Pendiente, Resuelta o null (todas).</param>
        /// <param name="origen">Almacén donde se comprueba, o null (todos).</param>
        Task<List<DiferenciaReposicionDTO>> Listar(string empresa, string estado, string origen);
        Task<DiferenciaReposicionDTO> Leer(string empresa, int id);
        Task<int> ContarPendientes(string empresa, string origen);
    }

    public interface IServicioDiferenciasReposicion
    {
        /// <summary>
        /// Después de confirmar la recepción: el ajuste del origen de cada diferencia, cada una en su transacción. Nunca lanza
        /// (lo que falle queda «Fallido» con el motivo y en ELMAH).
        /// </summary>
        Task<List<ResultadoAjusteDiferencia>> AjustarOrigenTrasRecibir(string empresa, IReadOnlyCollection<int> ids, string usuario);
        Task<List<DiferenciaReposicionDTO>> Listar(string empresa, string estado, string almacen, IPrincipal usuario);
        Task<DiferenciaReposicionDTO> Leer(string empresa, int id, IPrincipal usuario);
        Task<ContadorDiferenciasReposicionDTO> ContarPendientes(string empresa, string almacen, IPrincipal usuario);
        Task<ResultadoResolverDiferenciaReposicionDTO> Resolver(string empresa, int id, ResolverDiferenciaReposicionDTO peticion, IPrincipal usuario);
    }

    public class ServicioDiferenciasReposicion : IServicioDiferenciasReposicion, IDisposable
    {
        internal const string CLAVE_ALMACEN_USUARIO = "AlmacénPedidoVta";
        internal const int LONGITUD_COMENTARIO = 500;
        internal const string MENSAJE_SIN_PERMISO_VER =
            "Las diferencias de reposición las ven Almacén, Dirección y cada tienda las de su almacén.";

        private readonly IRepositorioDiferenciasReposicion repositorio;
        private readonly Func<bool> ajusteActivo;
        private readonly Func<string, string, string> almacenDelUsuario;

        public ServicioDiferenciasReposicion() : this(RepositorioDiferenciasReposicionSql.ConContextoPropio())
        {
        }

        public ServicioDiferenciasReposicion(IRepositorioDiferenciasReposicion repositorio) : this(repositorio, null, null)
        {
        }

        /// <param name="ajusteActivo">El interruptor (por defecto, <see cref="InterruptorAjusteDiferencias"/>).</param>
        /// <param name="almacenDelUsuario">(empresa, usuario sin dominio) → su AlmacénPedidoVta.</param>
        internal ServicioDiferenciasReposicion(IRepositorioDiferenciasReposicion repositorio, Func<bool> ajusteActivo,
            Func<string, string, string> almacenDelUsuario)
        {
            this.repositorio = repositorio;
            this.ajusteActivo = ajusteActivo ?? InterruptorAjusteDiferencias.EstaActivo;
            this.almacenDelUsuario = almacenDelUsuario
                ?? ((empresa, usuario) => Controllers.ParametrosUsuarioController.LeerParametro(empresa, usuario, CLAVE_ALMACEN_USUARIO));
        }

        public void Dispose()
        {
            (repositorio as IDisposable)?.Dispose();
        }

        // ---------------------------------------------------------------- al recibir

        public async Task<List<ResultadoAjusteDiferencia>> AjustarOrigenTrasRecibir(string empresa, IReadOnlyCollection<int> ids, string usuario)
        {
            var resultados = new List<ResultadoAjusteDiferencia>();
            foreach (int id in (ids ?? new int[0]).Distinct())
            {
                try
                {
                    ResultadoAjusteDiferencia resultado = await repositorio.EnTransaccion(t => AjustarUna(t, empresa, id, usuario)).ConfigureAwait(false);
                    if (resultado != null)
                    {
                        resultados.Add(resultado);
                    }
                }
                catch (Exception ex)
                {
                    string mensaje = Recortar("No se ha podido ajustar el stock del origen: " + MensajeDe(ex), LONGITUD_COMENTARIO);
                    ElmahHelper.Log(new Exception($"[Diferencias de reposición] Diferencia {id}: {mensaje}", ex));
                    try
                    {
                        _ = await repositorio.EnTransaccion(async t =>
                        {
                            await t.GuardarAjuste(id, AjustesDiferenciaReposicion.FALLIDO, null, mensaje).ConfigureAwait(false);
                            return true;
                        }).ConfigureAwait(false);
                    }
                    catch (Exception ex2)
                    {
                        ElmahHelper.Log(new Exception($"[Diferencias de reposición] No se pudo apuntar el fallo del ajuste de la diferencia {id}", ex2));
                    }
                    resultados.Add(new ResultadoAjusteDiferencia { Id = id, Ajuste = AjustesDiferenciaReposicion.FALLIDO, Mensaje = mensaje });
                }
            }
            return resultados;
        }

        private async Task<ResultadoAjusteDiferencia> AjustarUna(ITransaccionDiferenciasReposicion t, string empresa, int id, string usuario)
        {
            DiferenciaReposicionDTO fila = await t.Leer(empresa, id).ConfigureAwait(false);
            if (fila == null)
            {
                return null;
            }
            var resultado = new ResultadoAjusteDiferencia
            {
                Id = id, Producto = fila.Producto, Diferencia = fila.Diferencia, Origen = fila.Origen, Ajuste = fila.Ajuste, Mensaje = fila.AjusteMensaje
            };
            if (fila.Ajuste != null || fila.Estado != EstadosDiferenciaReposicion.PENDIENTE)
            {
                return resultado; // ya ajustada (o apagada): un reenvío no ajusta dos veces
            }
            if (!ajusteActivo())
            {
                await t.GuardarAjuste(id, AjustesDiferenciaReposicion.APAGADO, null, null).ConfigureAwait(false);
                resultado.Ajuste = AjustesDiferenciaReposicion.APAGADO;
                return resultado;
            }
            if (string.IsNullOrWhiteSpace(fila.Origen))
            {
                resultado.Ajuste = AjustesDiferenciaReposicion.FALLIDO;
                resultado.Mensaje = "No se sabe de qué almacén salió la reposición: no se ha ajustado el stock del origen.";
                await t.GuardarAjuste(id, resultado.Ajuste, null, resultado.Mensaje).ConfigureAwait(false);
                return resultado;
            }

            ResultadoMovimientoStock movimiento = await t.Mover(new MovimientoStockDiferencia
            {
                Empresa = empresa,
                Almacen = fila.Origen,
                Producto = fila.Producto,
                Cantidad = -fila.Diferencia,
                Traspaso = fila.Traspaso,
                Texto = $"Diferencia de reposición {fila.Traspaso} (recibido en {fila.Destino})",
                Usuario = usuario,
                PrimeroPendienteDeUbicar = false
            }).ConfigureAwait(false);
            resultado.Ajuste = AjustesDiferenciaReposicion.HECHO;
            resultado.Mensaje = SinHueco(movimiento, fila.Origen);
            await t.GuardarAjuste(id, resultado.Ajuste, movimiento?.NumeroOrdenExtracto, resultado.Mensaje).ConfigureAwait(false);
            return resultado;
        }

        // ---------------------------------------------------------------- consultas

        public async Task<List<DiferenciaReposicionDTO>> Listar(string empresa, string estado, string almacen, IPrincipal usuario)
        {
            string origen = AlmacenQuePuedeVer(usuario, empresa, almacen);
            string filtroEstado = NormalizarEstado(estado);
            List<DiferenciaReposicionDTO> filas = await repositorio.Listar(empresa, filtroEstado, origen).ConfigureAwait(false)
                ?? new List<DiferenciaReposicionDTO>();
            filas.ForEach(Completar);
            return filas;
        }

        public async Task<DiferenciaReposicionDTO> Leer(string empresa, int id, IPrincipal usuario)
        {
            DiferenciaReposicionDTO fila = await repositorio.Leer(empresa, id).ConfigureAwait(false);
            if (fila == null)
            {
                return null;
            }
            _ = AlmacenQuePuedeVer(usuario, empresa, fila.Origen);
            Completar(fila);
            return fila;
        }

        public async Task<ContadorDiferenciasReposicionDTO> ContarPendientes(string empresa, string almacen, IPrincipal usuario)
        {
            string origen = AlmacenQuePuedeVer(usuario, empresa, almacen);
            return new ContadorDiferenciasReposicionDTO
            {
                Almacen = origen,
                Pendientes = await repositorio.ContarPendientes(empresa, origen).ConfigureAwait(false)
            };
        }

        // ---------------------------------------------------------------- resolver

        public async Task<ResultadoResolverDiferenciaReposicionDTO> Resolver(string empresa, int id, ResolverDiferenciaReposicionDTO peticion, IPrincipal usuario)
        {
            string resolucion = ResolucionesDiferenciaReposicion.Normalizar(peticion?.Resolucion);
            if (resolucion == null)
            {
                throw new NestoBusinessException($"La resolución «{peticion?.Resolucion?.Trim()}» no existe: tiene que ser " +
                    string.Join(", ", ResolucionesDiferenciaReposicion.TODAS) + ".");
            }
            string comentario = string.IsNullOrWhiteSpace(peticion.Comentario) ? null : peticion.Comentario.Trim();
            if (comentario != null && comentario.Length > LONGITUD_COMENTARIO)
            {
                throw new NestoBusinessException($"El comentario no puede pasar de {LONGITUD_COMENTARIO} caracteres.");
            }
            string quien = UsuarioAuditoriaHelper.Resolver(usuario, null) ?? UsuarioAuditoriaHelper.DESCONOCIDO;
            bool activo = ajusteActivo();

            string mensaje = null;
            DiferenciaReposicionDTO resuelta = await repositorio.EnTransaccion(async t =>
            {
                DiferenciaReposicionDTO fila = await t.Leer(empresa, id).ConfigureAwait(false);
                if (fila == null)
                {
                    throw new NestoBusinessException($"No existe la diferencia de reposición {id}.") { StatusCode = HttpStatusCode.NotFound };
                }
                ComprobarPuedeResolver(usuario, empresa, fila);
                if (fila.Estado != EstadosDiferenciaReposicion.PENDIENTE)
                {
                    throw new NestoBusinessException($"La diferencia de {fila.Producto} de la reposición {fila.Traspaso} ya está resuelta " +
                        $"({fila.Resolucion}, por {fila.UsuarioResolucion}).") { StatusCode = HttpStatusCode.Conflict };
                }
                if (!ResolucionesDiferenciaReposicion.Posibles(fila.Diferencia).Contains(resolucion))
                {
                    throw new NestoBusinessException(resolucion == ResolucionesDiferenciaReposicion.NO_ESTABA
                        ? $"«No estaba» es para cuando se recibió de menos; en la reposición {fila.Traspaso} se recibió de más ({fila.Leido} de {fila.Enviado})."
                        : $"«Sobraba» es para cuando se recibió de más; en la reposición {fila.Traspaso} se recibió de menos ({fila.Leido} de {fila.Enviado}).");
                }

                PlanResolucion plan = Planificar(fila, resolucion);
                int? numeroOrigen = null;
                int? numeroDestino = null;
                if (!plan.MueveStock)
                {
                    mensaje = "No se ha tocado el stock: ya estaba bien.";
                }
                else if (!activo)
                {
                    mensaje = "No se ha tocado el stock porque el ajuste automático está apagado: " + plan.Descripcion(fila) + " Hágalo a mano si hace falta.";
                }
                else
                {
                    var avisos = new List<string>();
                    if (plan.Origen != 0)
                    {
                        ResultadoMovimientoStock movimiento = await t.Mover(Movimiento(empresa, fila, fila.Origen, plan.Origen, resolucion, quien)).ConfigureAwait(false);
                        numeroOrigen = movimiento?.NumeroOrdenExtracto;
                        avisos.Add(SinHueco(movimiento, fila.Origen));
                    }
                    if (plan.Destino != 0)
                    {
                        ResultadoMovimientoStock movimiento = await t.Mover(Movimiento(empresa, fila, fila.Destino, plan.Destino, resolucion, quien)).ConfigureAwait(false);
                        numeroDestino = movimiento?.NumeroOrdenExtracto;
                        avisos.Add(SinHueco(movimiento, fila.Destino));
                    }
                    mensaje = string.Join(" ", new[] { "Stock corregido: " + plan.Descripcion(fila) }.Concat(avisos.Where(a => a != null)));
                }
                await t.GuardarResolucion(id, resolucion, quien, comentario, numeroOrigen, numeroDestino).ConfigureAwait(false);
                return await t.Leer(empresa, id).ConfigureAwait(false);
            }).ConfigureAwait(false);

            Completar(resuelta);
            return new ResultadoResolverDiferenciaReposicionDTO { Diferencia = resuelta, Mensaje = mensaje };
        }

        /// <summary>Cuánto hay que mover ahora en el origen y en el destino (positivo entra, negativo sale).</summary>
        internal class PlanResolucion
        {
            public int Origen { get; set; }
            public int Destino { get; set; }
            public bool MueveStock => Origen != 0 || Destino != 0;

            public string Descripcion(DiferenciaReposicionDTO fila)
            {
                var partes = new List<string>();
                if (Origen != 0)
                {
                    partes.Add($"{(Origen > 0 ? "entran" : "salen")} {Math.Abs(Origen)} ud. de {fila.Producto} en {fila.Origen}");
                }
                if (Destino != 0)
                {
                    partes.Add($"{(Destino > 0 ? "entran" : "salen")} {Math.Abs(Destino)} ud. en {fila.Destino}");
                }
                return string.Join(" y ", partes) + ".";
            }
        }

        /// <summary>
        /// Lo que tiene que quedar respecto a como quedó la recepción (sin ajuste): «Confirmada», el origen con la diferencia
        /// devuelta (−Diferencia: lo que no salió vuelve, lo que salió de más se descuenta); «NoEstaba» y «Sobraba», el origen
        /// como lo dejó la salida (0); «ErrorRecepcion», el origen como lo dejó la salida y el destino con lo enviado
        /// (−Diferencia). A eso se le resta lo que ya hizo el ajuste al recibir (−Diferencia si se hizo). Puro.
        /// </summary>
        internal static PlanResolucion Planificar(DiferenciaReposicionDTO fila, string resolucion)
        {
            int yaAjustado = fila.Ajuste == AjustesDiferenciaReposicion.HECHO ? -fila.Diferencia : 0;
            int objetivoOrigen = resolucion == ResolucionesDiferenciaReposicion.CONFIRMADA ? -fila.Diferencia : 0;
            return new PlanResolucion
            {
                Origen = objetivoOrigen - yaAjustado,
                Destino = resolucion == ResolucionesDiferenciaReposicion.ERROR_RECEPCION ? -fila.Diferencia : 0
            };
        }

        private static MovimientoStockDiferencia Movimiento(string empresa, DiferenciaReposicionDTO fila, string almacen, int cantidad,
            string resolucion, string usuario)
        {
            string motivo;
            switch (resolucion)
            {
                case ResolucionesDiferenciaReposicion.NO_ESTABA: motivo = $"no estaba en {almacen}"; break;
                case ResolucionesDiferenciaReposicion.SOBRABA: motivo = $"sobraba en {almacen}"; break;
                case ResolucionesDiferenciaReposicion.ERROR_RECEPCION: motivo = $"error al recibir en {fila.Destino}"; break;
                default: motivo = $"comprobada en {almacen}"; break;
            }
            return new MovimientoStockDiferencia
            {
                Empresa = empresa,
                Almacen = almacen,
                Producto = fila.Producto,
                Cantidad = cantidad,
                Traspaso = fila.Traspaso,
                Texto = $"Diferencia de reposición {fila.Traspaso} ({motivo})",
                Usuario = usuario,
                // Al resolver, lo que se quita es lo que puso el ajuste al recibir («pendiente de ubicar») si sigue ahí
                PrimeroPendienteDeUbicar = true
            };
        }

        // ---------------------------------------------------------------- permisos

        private static bool EsDeAlmacenODireccion(IPrincipal usuario)
        {
            return usuario != null && (usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.ALMACEN) ||
                usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION));
        }

        private string AlmacenDe(IPrincipal usuario, string empresa)
        {
            string nombre = usuario?.Identity?.IsAuthenticated == true ? usuario.Identity.Name : null;
            if (string.IsNullOrWhiteSpace(nombre))
            {
                return null;
            }
            string sinDominio = nombre.Contains("\\") ? nombre.Substring(nombre.LastIndexOf('\\') + 1) : nombre;
            return almacenDelUsuario(empresa, sinDominio.Trim())?.Trim();
        }

        /// <summary>
        /// Almacén y Dirección ven todas (o las del almacén que pidan); los demás, solo las de su AlmacénPedidoVta (si no dicen
        /// almacén, el suyo; otro, 403).
        /// </summary>
        internal string AlmacenQuePuedeVer(IPrincipal usuario, string empresa, string almacen)
        {
            string pedido = string.IsNullOrWhiteSpace(almacen) ? null : almacen.Trim().ToUpperInvariant();
            if (EsDeAlmacenODireccion(usuario))
            {
                return pedido;
            }
            string suyo = AlmacenDe(usuario, empresa);
            if (string.IsNullOrWhiteSpace(suyo) || (pedido != null && !string.Equals(pedido, suyo, StringComparison.OrdinalIgnoreCase)))
            {
                throw new UnauthorizedAccessException(MENSAJE_SIN_PERMISO_VER);
            }
            return suyo.ToUpperInvariant();
        }

        /// <summary>Almacén y Dirección; o la gente del almacén de origen si no lleva control de ubicaciones (las tiendas).</summary>
        private void ComprobarPuedeResolver(IPrincipal usuario, string empresa, DiferenciaReposicionDTO fila)
        {
            if (EsDeAlmacenODireccion(usuario))
            {
                return;
            }
            string suyo = AlmacenDe(usuario, empresa);
            bool esDeSuAlmacen = !string.IsNullOrWhiteSpace(suyo) && string.Equals(suyo, fila.Origen?.Trim(), StringComparison.OrdinalIgnoreCase);
            if (!esDeSuAlmacen || fila.OrigenConControlUbicaciones)
            {
                throw new UnauthorizedAccessException(fila.OrigenConControlUbicaciones
                    ? $"Las diferencias de reposición de {fila.Origen} las resuelven Almacén y Dirección."
                    : $"Las diferencias de reposición de {fila.Origen} las resuelve la gente de ese almacén (o Almacén y Dirección).");
            }
        }

        // ---------------------------------------------------------------- textos

        private static string NormalizarEstado(string estado)
        {
            if (string.IsNullOrWhiteSpace(estado) || string.Equals(estado.Trim(), "Todas", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            if (string.Equals(estado.Trim(), EstadosDiferenciaReposicion.PENDIENTE, StringComparison.OrdinalIgnoreCase))
            {
                return EstadosDiferenciaReposicion.PENDIENTE;
            }
            if (string.Equals(estado.Trim(), EstadosDiferenciaReposicion.RESUELTA, StringComparison.OrdinalIgnoreCase))
            {
                return EstadosDiferenciaReposicion.RESUELTA;
            }
            throw new NestoBusinessException($"El estado «{estado.Trim()}» no existe: tiene que ser Pendiente, Resuelta o Todas.");
        }

        /// <summary>Lo que se calcula de la fila: qué pasó, qué comprobar y qué se puede elegir.</summary>
        internal static void Completar(DiferenciaReposicionDTO fila)
        {
            if (fila == null)
            {
                return;
            }
            int unidades = Math.Abs(fila.Diferencia);
            string ud = unidades == 1 ? "1 ud." : $"{unidades} ud.";
            string origen = string.IsNullOrWhiteSpace(fila.Origen) ? "el origen" : fila.Origen;
            fila.QuePaso = fila.Enviado == 0
                ? $"En {fila.Destino} se recibieron {fila.Leido} que no venían en la reposición."
                : $"En {fila.Destino} se recibieron {fila.Leido} de {fila.Enviado}.";
            string hueco = string.IsNullOrWhiteSpace(fila.HuecoSalida) ? "" : $" (salió del hueco {fila.HuecoSalida})";
            fila.QueComprobar = fila.Diferencia < 0
                ? $"Compruebe que {ud} sigue en {origen}{hueco}: no llegó a salir. Si no está, elija «No estaba»."
                : $"Compruebe que en {origen}{hueco} ya no queda {ud}: salió de más. Si sigue ahí, elija «Sobraba».";
            fila.ResolucionesPosibles = fila.Estado == EstadosDiferenciaReposicion.PENDIENTE
                ? ResolucionesDiferenciaReposicion.Posibles(fila.Diferencia)
                : new List<string>();
        }

        private static string SinHueco(ResultadoMovimientoStock movimiento, string almacen)
        {
            int sinHueco = movimiento?.UnidadesSinHueco ?? 0;
            return sinHueco > 0
                ? $"No se han encontrado en ningún hueco de {almacen} {sinHueco} ud. que había que quitar de Ubicaciones: revíselo."
                : null;
        }

        internal static string MensajeDe(Exception ex)
        {
            Exception actual = ex;
            while (actual?.InnerException != null && actual is AggregateException)
            {
                actual = actual.InnerException;
            }
            return actual?.Message ?? "error desconocido";
        }

        private static string Recortar(string texto, int maximo)
        {
            return texto == null ? null : (texto.Length > maximo ? texto.Substring(0, maximo) : texto);
        }
    }
}
