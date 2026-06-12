using NestoAPI.Infraestructure.ValidadoresPedido;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure
{
    public class GestorPrecios
    {
        public static bool calcularAplicarDescuento(Producto producto)
        {
            return calcularAplicarDescuento(producto.Aplicar_Dto, producto.Familia, producto.SubGrupo);
        }

        public static bool calcularAplicarDescuento(bool aplicarDescuento, string familia, string subGrupo)
        {
            // Esto podríamos sacarlo a otra clase que sea más fácil de mantener
            // if (!aplicarDescuento || familia == "Ramason" || subGrupo == "ACP")
            if (!aplicarDescuento)
            {
                return false;
            }
            else
            {
                return true;
            }

        }

        public static void calcularDescuentoProducto(PrecioDescuentoProducto datos)
        {
            using (NVEntities db = new NVEntities())
            {
                calcularDescuentoProducto(datos, db);
            }
        }

        // Issue #229: en DescuentosProducto solo puede haber una fila aplicable por filtro.
        // Si hay más de una (p.ej. dos familias que solo difieren en mayúsculas y que SQL Server
        // considera iguales), es un error de datos: se lanza un error que indica qué está duplicado
        // para que el usuario lo corrija, en vez del genérico "La secuencia contiene más de un elemento".
        private static DescuentosProducto BuscarDescuentoUnico(NVEntities db, System.Linq.Expressions.Expression<Func<DescuentosProducto, bool>> filtro, PrecioDescuentoProducto datos, string ambito)
        {
            try
            {
                return db.DescuentosProductoes.SingleOrDefault(filtro);
            }
            catch (InvalidOperationException ex)
            {
                throw new Exceptions.DescuentosDuplicadosException(ambito, datos.producto.Empresa?.Trim(), datos.cliente?.Trim(), ex);
            }
        }

        // Issue #229: sobrecarga con el contexto inyectado para poder testear el control de duplicados.
        internal static void calcularDescuentoProducto(PrecioDescuentoProducto datos, NVEntities db)
        {

            DescuentosProducto dtoProducto;

            datos.descuentoCalculado = 0;
            datos.precioCalculado = (decimal)datos.producto.PVP;

            datos.aplicarDescuento = calcularAplicarDescuento(datos.aplicarDescuento, datos.producto.Familia, datos.producto.SubGrupo);

            // En Nesto Viejo, si no tiene el aplicar descuento marcado, solo calcula precios especiales, pero no descuentos
            // Ahora hacemos que no calcule nada, por eso lo pongo aquí arriba.
            if (!datos.aplicarDescuento)
            {
                return;
            }


            {
                // AQUÍ CALCULA PRECIOS, NO DESCUENTOS
                //select precio from descuentosproducto with (nolock) where [nº cliente]='15191     ' and contacto='0  ' and [nº producto]= '29487' and empresa='1  ' AND CANTIDADMÍNIMA<=1

                dtoProducto = BuscarDescuentoUnico(db, d => d.Empresa == datos.producto.Empresa && d.Nº_Cliente == datos.cliente && d.Contacto == datos.contacto && d.Nº_Producto == datos.producto.Número && d.CantidadMínima <= datos.cantidad,
                    datos, $"el precio especial del producto {datos.producto.Número?.Trim()} del cliente {datos.cliente?.Trim()} (contacto {datos.contacto?.Trim()})");
                if (dtoProducto != null && dtoProducto.Precio < datos.precioCalculado)
                {
                    datos.precioCalculado = (decimal)dtoProducto.Precio;
                }
                //select precio from descuentosproducto with (nolock) where [nº cliente]='15191     '  and [nº producto]= '29487' and empresa='1  ' AND CantidadMínima<=1
                //select recargopvp from clientes with (nolock) where empresa='1  ' and [nº cliente]='15191     ' and contacto='0  '
                //select top 1 precio,cantidadminima from descuentosproducto where cantidadminíma<=1 and  empresa='1  ' and [Nº Producto]='29352' and [nº cliente]='15191     ' and contacto='0  ' order by cantidadminima desc
                //select top 1 precio,cantidadminima from descuentosproducto where cantidadminíma<=1 and  empresa='1  ' and [Nº Producto]='29352' and [nº cliente]='15191     '  order by cantidadminima desc
                dtoProducto = BuscarDescuentoUnico(db, d => d.Empresa == datos.producto.Empresa && d.Nº_Cliente == datos.cliente && d.Nº_Producto == datos.producto.Número && d.CantidadMínima <= datos.cantidad,
                    datos, $"el precio especial del producto {datos.producto.Número?.Trim()} del cliente {datos.cliente?.Trim()}");
                if (dtoProducto != null && dtoProducto.Precio < datos.precioCalculado)
                {
                    datos.precioCalculado = (decimal)dtoProducto.Precio;
                }
                //select top 1 precio,cantidadminima from descuentosproducto where cantidadminíma<=1 and  empresa='1  ' and [Nº Producto]='29352' and [nº cliente] is null and [nºproveedor] is null order by cantidadminima desc
                dtoProducto = db.DescuentosProductoes.OrderByDescending(d => d.CantidadMínima).FirstOrDefault(d => d.Empresa == datos.producto.Empresa && d.Nº_Producto == datos.producto.Número && d.CantidadMínima <= datos.cantidad && d.Nº_Cliente == null && d.NºProveedor == null);
                if (dtoProducto != null && dtoProducto.Precio < datos.precioCalculado)
                {
                    datos.precioCalculado = (decimal)dtoProducto.Precio;
                }

                // CALCULA DESCUENTOS
                //select * from descuentosproducto where empresa='1  ' and [nº producto]='29352' and [nº cliente] is null and nºproveedor is null and familia is null
                //select * from descuentosproducto where empresa='1  ' and grupoproducto='PEL' and [nº cliente] is null and  nºproveedor is null and familia is null

                //select * from descuentosproducto where empresa='1  ' and grupoproducto='PEL' and [nº cliente]='15191     ' and nºproveedor is null and familia is null

                //select isnull(max(descuento),0) from descuentosproducto where [nº cliente]='15191     ' and empresa='1  ' and grupoproducto='PEL' and cantidadmínima<=1 and familia is null and nºproveedor is null
                dtoProducto = BuscarDescuentoUnico(db, d => d.Empresa == datos.producto.Empresa && d.Familia == datos.producto.Familia && d.CantidadMínima <= datos.cantidad && d.NºProveedor == null && d.GrupoProducto == null && d.FiltroProducto == null && d.Nº_Cliente == null,
                    datos, $"la familia {datos.producto.Familia?.Trim()}");
                if (dtoProducto != null) // && dtoProducto.Descuento > datos.descuentoCalculado)
                {
                    datos.descuentoCalculado = dtoProducto.Descuento;
                }

                dtoProducto = BuscarDescuentoUnico(db, d => d.Empresa == datos.producto.Empresa && d.Familia == datos.producto.Familia && d.CantidadMínima <= datos.cantidad && d.NºProveedor == null && d.GrupoProducto == datos.producto.Grupo && d.FiltroProducto == null && d.Nº_Cliente == null,
                    datos, $"la familia {datos.producto.Familia?.Trim()} (grupo {datos.producto.Grupo?.Trim()})");
                if (dtoProducto != null) // && dtoProducto.Descuento > datos.descuentoCalculado)
                {
                    datos.descuentoCalculado = dtoProducto.Descuento;
                }

                dtoProducto = BuscarDescuentoUnico(db, d => d.Empresa == datos.producto.Empresa && d.Nº_Cliente == datos.cliente && d.Familia == null && d.CantidadMínima <= datos.cantidad && d.NºProveedor == null && d.GrupoProducto == datos.producto.Grupo,
                    datos, $"el grupo {datos.producto.Grupo?.Trim()} del cliente {datos.cliente?.Trim()}");
                if (dtoProducto != null) // && dtoProducto.Descuento > datos.descuentoCalculado)
                {
                    datos.descuentoCalculado = dtoProducto.Descuento;
                }
                //select * from descuentosproducto where empresa='1  ' and familia='Lisap     ' and [nº cliente]='15191     ' and nºproveedor is null and grupoproducto is null
                //select isnull(max(descuento),0) from descuentosproducto where [nº cliente]='15191     ' and empresa='1  ' and familia='Lisap     ' and cantidadmínima<=1 and nºproveedor is null  and grupoproducto is null
                dtoProducto = BuscarDescuentoUnico(db, d => d.Empresa == datos.producto.Empresa && d.Nº_Cliente == datos.cliente && d.Familia == datos.producto.Familia && d.CantidadMínima <= datos.cantidad && d.NºProveedor == null && d.GrupoProducto == null && d.FiltroProducto == null,
                    datos, $"la familia {datos.producto.Familia?.Trim()} del cliente {datos.cliente?.Trim()}");
                if (dtoProducto != null)// && dtoProducto.Descuento > datos.descuentoCalculado)
                {
                    datos.descuentoCalculado = dtoProducto.Descuento;
                }
                // Descuento por familia con filtro y contacto
                // Issue #229: el guard d.FiltroProducto != null replica la semántica de SQL (LIKE NULL no
                // matchea ninguna fila) y evita el ArgumentNullException de StartsWith(null) en memoria.
                dtoProducto = BuscarDescuentoUnico(db, d => d.Empresa == datos.producto.Empresa && d.Nº_Cliente == datos.cliente && d.Contacto == datos.contacto && d.Familia == datos.producto.Familia && d.FiltroProducto != null && datos.producto.Nombre.StartsWith(d.FiltroProducto) && d.CantidadMínima <= datos.cantidad && d.NºProveedor == null && d.GrupoProducto == null,
                    datos, $"la familia {datos.producto.Familia?.Trim()} con filtro de producto del cliente {datos.cliente?.Trim()} (contacto {datos.contacto?.Trim()})");
                if (dtoProducto != null) // && dtoProducto.Descuento > datos.descuentoCalculado)
                {
                    datos.descuentoCalculado = dtoProducto.Descuento;
                }
                // Descuento por familia con filtro sin contacto
                if (dtoProducto == null) // solo entramos si no ha encontrado el descuento con el contacto
                {
                    dtoProducto = BuscarDescuentoUnico(db, d => d.Empresa == datos.producto.Empresa && d.Nº_Cliente == datos.cliente && d.Contacto == null && d.Familia == datos.producto.Familia && d.FiltroProducto != null && datos.producto.Nombre.StartsWith(d.FiltroProducto) && d.CantidadMínima <= datos.cantidad && d.NºProveedor == null && d.GrupoProducto == null,
                        datos, $"la familia {datos.producto.Familia?.Trim()} con filtro de producto del cliente {datos.cliente?.Trim()}");
                    if (dtoProducto != null) // && dtoProducto.Descuento > datos.descuentoCalculado)
                    {
                        datos.descuentoCalculado = dtoProducto.Descuento;
                    }
                }
                //select * from descuentosproducto where empresa='1  ' and familia='Lisap     ' and [nº cliente]='15191     ' and grupoproducto='PEL' and nºproveedor is null
                dtoProducto = BuscarDescuentoUnico(db, d => d.Empresa == datos.producto.Empresa && d.Nº_Cliente == datos.cliente && d.Familia == datos.producto.Familia && d.CantidadMínima <= datos.cantidad && d.NºProveedor == null && d.GrupoProducto == datos.producto.Grupo,
                    datos, $"la familia {datos.producto.Familia?.Trim()} (grupo {datos.producto.Grupo?.Trim()}) del cliente {datos.cliente?.Trim()}");
                if (dtoProducto != null) // && dtoProducto.Descuento > datos.descuentoCalculado)
                {
                    datos.descuentoCalculado = dtoProducto.Descuento;
                }

                dtoProducto = db.DescuentosProductoes.OrderByDescending(d => d.CantidadMínima).FirstOrDefault(d => d.Empresa == datos.producto.Empresa && d.Producto.Número == datos.producto.Número && d.Nº_Cliente == null && d.Familia == null && d.CantidadMínima <= datos.cantidad && d.NºProveedor == null && d.GrupoProducto == null);
                if (dtoProducto != null && dtoProducto.Descuento > datos.descuentoCalculado)
                {
                    datos.descuentoCalculado = dtoProducto.Descuento;
                }

                //select * from descuentosproducto where empresa='1  ' and [nº producto]='29352' and [nº cliente]='15191     ' and nºproveedor is null and familia is null
                dtoProducto = BuscarDescuentoUnico(db, d => d.Empresa == datos.producto.Empresa && d.Nº_Cliente == datos.cliente && d.Nº_Producto == datos.producto.Número && d.CantidadMínima <= datos.cantidad && d.NºProveedor == null && d.Familia == null,
                    datos, $"el descuento del producto {datos.producto.Número?.Trim()} del cliente {datos.cliente?.Trim()}");
                if (dtoProducto != null)// && dtoProducto.Descuento > datos.descuentoCalculado)
                {
                    datos.descuentoCalculado = dtoProducto.Descuento;
                }

                // Carlos 25/04/19: si es material promocional va con el 100% de descuento
                if (datos.producto.SubGrupo == Constantes.Productos.SUBGRUPO_MUESTRAS)
                {
                    datos.descuentoCalculado = 1;
                }

                if (datos.precioCalculado < datos.producto.PVP * (1 - datos.descuentoCalculado))
                {
                    datos.descuentoCalculado = 0;
                }
                else
                {
                    datos.precioCalculado = (decimal)datos.producto.PVP;
                }

                // Si quisiéramos comprobar también las condiciones que tiene en ficha, descomentar la siguiente línea
                // comprobarCondiciones(datos);
            }
        }

        private static void cargarListaCondiciones()
        {
            // Rellenamos la lista estática de las condiciones que queremos comprobar.
            // Se implenta de esta forma para que sea sencillo en el futuro poner o 
            // o quitar condiciones, ya que se implementa como clases individuales que
            // implementan el interfaz ICondicionPrecioDescuento
            listaCondiciones.Add(new OtrosAparatosNoPuedeLlevarDescuento());
            listaCondiciones.Add(new DuNoPuedeLlevarCualquierDescuento());
            listaCondiciones.Add(new RamasonNoPuedeLlevarNingunDescuento());
            listaCondiciones.Add(new CerasConPrecioMinimo());
            listaCondiciones.Add(new ThuyaNoPuedeLlevarCualquierDescuento());
        }

        private static void CargarListaValidadoresPedido()
        {
            listaValidadoresDenegacion.Add(new ValidadorOfertasPermitidas());
            listaValidadoresDenegacion.Add(new ValidadorDescuentosPermitidos());
            listaValidadoresDenegacion.Add(new ValidadorOtrosAparatosSiempreSinDescuento());
            listaValidadoresDenegacion.Add(new ValidadorLimiteRegalos());
            listaValidadoresDenegacion.Add(new ValidadorOfertaSinBeneficio());

            listaValidadoresAceptacion.Add(new ValidadorOfertasCombinadas());
            listaValidadoresAceptacion.Add(new ValidadorOfertasEscalonadas());
            listaValidadoresAceptacion.Add(new ValidadorMuestrasYMaterialPromocional());
            listaValidadoresAceptacion.Add(new ValidadorRegalosTiendaOnline());
            listaValidadoresAceptacion.Add(new ValidadorDescuentoTiendaOnline());
            listaValidadoresAceptacion.Add(new ValidadorGanavisiones());
            listaValidadoresAceptacion.Add(new ValidadorRegaloPorImportePedido());
        }

        public static bool comprobarCondiciones(PrecioDescuentoProducto datos)
        {
            // Primero miramos si ese precio está en las oferta (tiene metidos los precios generales para todos los clientes)
            if (!datos.descuentosRellenos)
            {
                using (NVEntities db = new NVEntities())
                {
                    // Lo hacemos así para que se pueda rellenar por fuera para los test
                    datos.descuentosProducto = db.DescuentosProductoes.OrderByDescending(d => d.CantidadMínima).Where(d => d.Empresa == datos.producto.Empresa && d.Nº_Producto == datos.producto.Número && d.CantidadMínima <= datos.cantidad && d.Nº_Cliente == null && d.NºProveedor == null).ToList();
                }
            }
            if (datos.descuentosProducto != null)
            {
                DescuentosProducto dtoProducto = datos.descuentosProducto.OrderByDescending(d => d.CantidadMínima).FirstOrDefault(d => d.CantidadMínima <= datos.cantidad);
                if (dtoProducto != null && dtoProducto.Precio * (1 - dtoProducto.Descuento) <= datos.producto.PVP * (1 - datos.descuentoReal))
                {
                    return true;
                }
            }


            // Recorre listaCondiciones y mira una a una si "datos" cumplen todas las condiciones
            // Devuelve true si cumple todas y false si alguna no se cumple
            // Las que no se cumplen son corregidas durante la comprobación
            if (listaCondiciones == null || listaCondiciones.Count == 0)
            {
                cargarListaCondiciones();
            }

            bool cumpleTodasLasCondiciones = true;

            // Cambiar el for por un while para mejorar rendimiento
            foreach (ICondicionPrecioDescuento condicion in listaCondiciones)
            {
                cumpleTodasLasCondiciones = condicion.precioAceptado(datos) && cumpleTodasLasCondiciones;
            }
            ;
            return cumpleTodasLasCondiciones;
        }

        public static List<ICondicionPrecioDescuento> listaCondiciones = new List<ICondicionPrecioDescuento>();

        // Dos listas de validadores:
        // - Denegación: son aquellos que buscan los pedidos que NO son válidos
        // - Aceptación: una vez un validador de denegación ha encontrado un pedido que NO es válido
        //   se pasa el validador de aceptación por si hay algún motivo para que SÍ sea válido
        public static List<IValidadorDenegacion> listaValidadoresDenegacion = new List<IValidadorDenegacion>();
        public static List<IValidadorAceptacion> listaValidadoresAceptacion = new List<IValidadorAceptacion>();


        // Es para poder sobreescribirlo en los tests
        public static IServicioPrecios servicio = new ServicioPrecios();

        // Método para validar todas las ofertas de un pedido        
        public static RespuestaValidacion EsPedidoValido(PedidoVentaDTO pedido)
        {
            RespuestaValidacion respuesta = new RespuestaValidacion
            {
                ValidacionSuperada = true
            };

            if (pedido.cliente == Constantes.ClientesEspeciales.EL_EDEN)
            {
                return respuesta;
            }

            if (listaValidadoresDenegacion == null || listaValidadoresDenegacion.Count == 0)
            {
                CargarListaValidadoresPedido();
            }

            List<string> erroresAcumulados = new List<string>();
            string ultimoMotivoExitoso = null;
            bool hayMotivoDeValidadorAceptacion = false; // NUEVO

            foreach (IValidadorDenegacion validador in listaValidadoresDenegacion)
            {
                RespuestaValidacion respuestaValidacion = validador.EsPedidoValido(pedido, servicio);

                // Guardamos mensajes informativos cuando la validación pasa
                // PERO no sobrescribimos mensajes de validadores de aceptación
                if (respuestaValidacion.ValidacionSuperada && !string.IsNullOrEmpty(respuestaValidacion.Motivo) && !hayMotivoDeValidadorAceptacion)
                {
                    ultimoMotivoExitoso = respuestaValidacion.Motivo;
                }

                if (!respuestaValidacion.ValidacionSuperada)
                {
                    // Procesamos CADA error encontrado por el validador
                    if (respuestaValidacion.Errores != null && respuestaValidacion.Errores.Any())
                    {
                        foreach (var error in respuestaValidacion.Errores)
                        {
                            RespuestaValidacion respuestaAceptacion = null;
                            if (!error.AutorizadaDenegadaExpresamente)
                            {
                                respuestaAceptacion = GestorPrecios.ComprobarValidadoresDeAceptacion(pedido, error.ProductoId);
                                if (respuestaAceptacion.ValidacionSuperada)
                                {
                                    // Este error está justificado - guardamos el motivo de aceptación
                                    if (!string.IsNullOrEmpty(respuestaAceptacion.Motivo))
                                    {
                                        ultimoMotivoExitoso = respuestaAceptacion.Motivo;
                                        hayMotivoDeValidadorAceptacion = true; // NUEVO: marcamos que viene de aceptación
                                    }
                                    continue;
                                }
                            }

                            // Acumulamos el error no justificado. Si un validador de aceptación reconoció
                            // el producto y dio un motivo específico (p. ej. "supera el 5 % del pedido"),
                            // lo preferimos al genérico de denegación ("No se encuentra autorizado el 100 %").
                            string motivoError = (respuestaAceptacion != null && respuestaAceptacion.MotivoEspecifico
                                    && !string.IsNullOrEmpty(respuestaAceptacion.Motivo))
                                ? respuestaAceptacion.Motivo
                                : error.Motivo;
                            if (!string.IsNullOrEmpty(motivoError))
                            {
                                erroresAcumulados.Add(motivoError);
                            }
                        }
                    }
                    else
                    {
                        // Compatibilidad con validadores que no usan Errores
                        RespuestaValidacion respuestaAceptacion = null;
                        if (!respuestaValidacion.AutorizadaDenegadaExpresamente)
                        {
                            respuestaAceptacion = GestorPrecios.ComprobarValidadoresDeAceptacion(pedido, respuestaValidacion.ProductoId);
                            if (respuestaAceptacion.ValidacionSuperada)
                            {
                                if (!string.IsNullOrEmpty(respuestaAceptacion.Motivo))
                                {
                                    ultimoMotivoExitoso = respuestaAceptacion.Motivo;
                                    hayMotivoDeValidadorAceptacion = true; // NUEVO: marcamos que viene de aceptación
                                }
                                continue;
                            }
                        }

                        // Preferimos el motivo específico del validador de aceptación al genérico de denegación.
                        string motivoDenegacion = (respuestaAceptacion != null && respuestaAceptacion.MotivoEspecifico
                                && !string.IsNullOrEmpty(respuestaAceptacion.Motivo))
                            ? respuestaAceptacion.Motivo
                            : respuestaValidacion.Motivo;
                        if (!string.IsNullOrEmpty(motivoDenegacion))
                        {
                            erroresAcumulados.Add(motivoDenegacion);
                        }
                    }
                }
            }

            // Consolidamos todos los errores encontrados
            erroresAcumulados = erroresAcumulados.Distinct().ToList();
            if (erroresAcumulados.Any())
            {
                respuesta.ValidacionSuperada = false;
                respuesta.Motivos = erroresAcumulados;
            }
            else if (!string.IsNullOrEmpty(ultimoMotivoExitoso))
            {
                // Si no hay errores pero hay un mensaje exitoso, lo guardamos
                respuesta.Motivo = ultimoMotivoExitoso;
            }

            return respuesta;
        }

        public static RespuestaValidacion ComprobarValidadoresDeAceptacion(PedidoVentaDTO pedido, string numeroProducto)
        {
            if (listaValidadoresAceptacion == null || listaValidadoresAceptacion.Count == 0)
            {
                CargarListaValidadoresPedido();
            }

            RespuestaValidacion respuesta = new RespuestaValidacion
            {
                ValidacionSuperada = false,
                Motivo = "No hay ninguna oferta que permita poner el producto " + numeroProducto + " a ese precio"
            };

            // Si ningún validador acepta, preferimos devolver el rechazo de un validador que "reconozca"
            // el producto (MotivoEspecifico) en vez del genérico del último de la lista: así el pipeline
            // puede dar un mensaje útil al usuario (p. ej. "supera el 5 % del pedido").
            RespuestaValidacion rechazoEspecifico = null;
            foreach (IValidadorAceptacion validador in listaValidadoresAceptacion)
            {
                respuesta = validador.EsPedidoValido(pedido, numeroProducto, servicio);
                if (respuesta.ValidacionSuperada)
                {
                    return respuesta;
                }
                if (respuesta.MotivoEspecifico && rechazoEspecifico == null)
                {
                    rechazoEspecifico = respuesta;
                }
            }

            return rechazoEspecifico ?? respuesta;
        }
    }

    public class PrecioDescuentoProducto
    {
        public decimal precioCalculado;
        public decimal descuentoCalculado;
        public Producto producto;
        public string cliente;
        public string contacto;
        public short cantidad;
        public short cantidadOferta;
        public bool aplicarDescuento;
        public string motivo; // cadena para mostrar en la interfaz de usuario
        public decimal? descuentoReal
        {
            get
            {
                decimal dividendo = precioCalculado * (1 - descuentoCalculado) * cantidad;
                decimal divisor = (decimal)producto.PVP * (cantidad + cantidadOferta);
                return divisor != 0 ? 1 - (dividendo / divisor) : 0;
            }
        }
        public decimal precioCalculadoDeFicha
        {
            get
            {
                decimal? precioProducto = producto.PVP;
                decimal? descuentoFinal = 1 - descuentoReal;
                double baseRedondear = (double)(precioProducto * descuentoFinal);
                return (decimal)Math.Round(baseRedondear, 2, MidpointRounding.AwayFromZero);
            }

        }
        public bool descuentosRellenos;
        public List<DescuentosProducto> descuentosProducto;
    }

    public class RespuestaValidacion
    {
        private List<string> _motivos = new List<string>();

        public bool ValidacionSuperada { get; set; }

        public List<string> Motivos
        {
            get => _motivos;
            set => _motivos = value ?? new List<string>();
        }

        // Para mantener el ProductoId de cada error
        public List<ErrorValidacion> Errores { get; set; } = new List<ErrorValidacion>();

        public string Motivo
        {
            get
            {
                if (!Motivos.Any())
                {
                    return null;
                }

                if (Motivos.Count == 1)
                {
                    return Motivos[0];
                }

                return string.Join(Environment.NewLine,
                    Motivos.Select((m, i) => $"• {m}"));
            }
            set
            {
                if (!string.IsNullOrEmpty(value))
                {
                    _motivos = new List<string> { value };
                }
            }
        }

        public string ProductoId { get; set; }
        public bool AutorizadaDenegadaExpresamente { get; set; }

        // Carlos 04/06/26: cuando un validador de ACEPTACIÓN rechaza un producto que él "reconoce"
        // (p. ej. el de muestras sabe que es material promocional y el motivo es que supera el 5 %),
        // marca su rechazo como específico. El pipeline (EsPedidoValido) prefiere este motivo concreto
        // al genérico de denegación ("No se encuentra autorizado el descuento del 100 %"), que no le
        // dice al usuario QUÉ pasa. Los rechazos genéricos lo dejan en false (comportamiento previo).
        public bool MotivoEspecifico { get; set; }
    }

    public class ErrorValidacion
    {
        public string Motivo { get; set; }
        public string ProductoId { get; set; }
        public bool AutorizadaDenegadaExpresamente { get; set; }
    }

    public interface ICondicionPrecioDescuento
    {
        // Pasamos datos del producto para ver qué precio mínimo se le puede dejar.
        // En caso de ser correctos todos los datos que hemos pasado, el procedimiento
        // devolverá true sin modificar "precio", y en caso de no poder ser esas condiciones
        // devolverá false y se modificarán los campos de "precio" que sea necesario para
        // que se pueda producir la venta cumpliendo nuestras condiciones.
        bool precioAceptado(PrecioDescuentoProducto precio);
    }

    public interface IValidadorDenegacion
    {
        RespuestaValidacion EsPedidoValido(PedidoVentaDTO pedido, IServicioPrecios servicio);
    }

    public interface IValidadorAceptacion
    {
        RespuestaValidacion EsPedidoValido(PedidoVentaDTO pedido, string numeroProducto, IServicioPrecios servicio);
    }

}
