// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace System.Net.Sockets
{
    public partial class Socket
    {
        /// <summary>Cached instance for receive operations that return <see cref="ValueTask{Int32}"/>. Also used for ConnectAsync operations.</summary>
        private AwaitableSocketAsyncEventArgs? _singleBufferReceiveEventArgs;
        /// <summary>Cached instance for send operations that return <see cref="ValueTask{Int32}"/>. Also used for AcceptAsync operations.</summary>
        private AwaitableSocketAsyncEventArgs? _singleBufferSendEventArgs;

        /// <summary>Cached instance for receive operations that return <see cref="Task{Int32}"/>.</summary>
        private TaskSocketAsyncEventArgs<int>? _multiBufferReceiveEventArgs;
        /// <summary>Cached instance for send operations that return <see cref="Task{Int32}"/>.</summary>
        private TaskSocketAsyncEventArgs<int>? _multiBufferSendEventArgs;

        /// <summary>
        /// Accepts an incoming connection.
        /// </summary>
        /// <returns>An asynchronous task that completes with the accepted Socket.</returns>
        public Task<Socket> AcceptAsync() => AcceptAsync((Socket?)null, CancellationToken.None).AsTask();

        /// <summary>
        /// Accepts an incoming connection.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
        /// <returns>An asynchronous task that completes with the accepted Socket.</returns>
        public ValueTask<Socket> AcceptAsync(CancellationToken cancellationToken) => AcceptAsync((Socket?)null, cancellationToken);

        /// <summary>
        /// Accepts an incoming connection.
        /// </summary>
        /// <param name="acceptSocket">The socket to use for accepting the connection.</param>
        /// <returns>An asynchronous task that completes with the accepted Socket.</returns>
        public Task<Socket> AcceptAsync(Socket? acceptSocket) => AcceptAsync(acceptSocket, CancellationToken.None).AsTask();

        /// <summary>
        /// Accepts an incoming connection.
        /// </summary>
        /// <param name="acceptSocket">The socket to use for accepting the connection.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
        /// <returns>An asynchronous task that completes with the accepted Socket.</returns>
        public ValueTask<Socket> AcceptAsync(Socket? acceptSocket, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return ValueTask.FromCanceled<Socket>(cancellationToken);
            }

            AwaitableSocketAsyncEventArgs saea =
                Interlocked.Exchange(ref _singleBufferSendEventArgs, null) ??
                new AwaitableSocketAsyncEventArgs(this, isReceiveForCaching: false);

            Debug.Assert(saea.BufferList is null);
            Debug.Assert(saea.AcceptSocket is null);
            saea.SetBuffer(null, 0, 0);
            saea.AcceptSocket = acceptSocket;
            saea.WrapExceptionsForNetworkStream = false;
            return saea.AcceptAsync(this, cancellationToken);
        }

        /// <summary>
        /// Establishes a connection to a remote host.
        /// </summary>
        /// <param name="remoteEP">The endpoint to connect to.</param>
        /// <returns>An asynchronous task that completes when the connection is established.</returns>
        public Task ConnectAsync(EndPoint remoteEP) => ConnectAsync(remoteEP, default).AsTask();

        /// <summary>
        /// Establishes a connection to a remote host.
        /// </summary>
        /// <param name="remoteEP">The endpoint to connect to.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
        /// <returns>An asynchronous task that completes when the connection is established.</returns>
        public ValueTask ConnectAsync(EndPoint remoteEP, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return ValueTask.FromCanceled(cancellationToken);
            }

            // Use _singleBufferReceiveEventArgs so the AwaitableSocketAsyncEventArgs can be re-used later for receives.
            AwaitableSocketAsyncEventArgs saea =
                Interlocked.Exchange(ref _singleBufferReceiveEventArgs, null) ??
                new AwaitableSocketAsyncEventArgs(this, isReceiveForCaching: true);

            saea.RemoteEndPoint = remoteEP;

            return saea.ConnectAsync(this, cancellationToken);
        }

        /// <summary>
        /// Establishes a connection to a remote host.
        /// </summary>
        /// <param name="address">The IPAddress of the remote host to connect to.</param>
        /// <param name="port">The port on the remote host to connect to.</param>
        /// <returns>An asynchronous task that completes when the connection is established.</returns>
        public Task ConnectAsync(IPAddress address, int port) => ConnectAsync(new IPEndPoint(address, port));

        /// <summary>
        /// Establishes a connection to a remote host.
        /// </summary>
        /// <param name="address">The IPAddress of the remote host to connect to.</param>
        /// <param name="port">The port on the remote host to connect to.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
        /// <returns>An asynchronous task that completes when the connection is established.</returns>
        public ValueTask ConnectAsync(IPAddress address, int port, CancellationToken cancellationToken) => ConnectAsync(new IPEndPoint(address, port), cancellationToken);

        /// <summary>
        /// Establishes a connection to a remote host.
        /// </summary>
        /// <param name="addresses">A list of IPAddresses for the remote host that will be used to attempt to connect to the remote host.</param>
        /// <param name="port">The port on the remote host to connect to.</param>
        /// <returns>An asynchronous task that completes when the connection is established.</returns>
        public Task ConnectAsync(IPAddress[] addresses, int port) => ConnectAsync(addresses, port, CancellationToken.None).AsTask();

        /// <summary>
        /// Establishes a connection to a remote host.
        /// </summary>
        /// <param name="addresses">A list of IPAddresses for the remote host that will be used to attempt to connect to the remote host.</param>
        /// <param name="port">The port on the remote host to connect to.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
        /// <returns>An asynchronous task that completes when the connection is established.</returns>
        public ValueTask ConnectAsync(IPAddress[] addresses, int port, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            ArgumentNullException.ThrowIfNull(addresses);

            if (addresses.Length == 0)
            {
                throw new ArgumentException(SR.net_invalidAddressList, nameof(addresses));
            }

            if (!TcpValidationHelpers.ValidatePortNumber(port))
            {
                throw new ArgumentOutOfRangeException(nameof(port));
            }

            if (_isListening)
            {
                throw new InvalidOperationException(SR.net_sockets_mustnotlisten);
            }

            if (_isConnected)
            {
                throw new SocketException((int)SocketError.IsConnected);
            }

            ValidateForMultiConnect();

            return Core(addresses, port, cancellationToken);

            async ValueTask Core(IPAddress[] addresses, int port, CancellationToken cancellationToken)
            {
                Exception? lastException = null;
                IPEndPoint? endPoint = null;
                foreach (IPAddress address in addresses)
                {
                    try
                    {
                        if (endPoint is null)
                        {
                            endPoint = new IPEndPoint(address, port);
                        }
                        else
                        {
                            endPoint.Address = address;
                            Debug.Assert(endPoint.Port == port);
                        }

                        await ConnectAsync(endPoint, cancellationToken).ConfigureAwait(false);
                        return;
                    }
                    catch (Exception ex) when (CanProceedWithMultiConnect && ex is not OperationCanceledException)
                    {
                        lastException = ex;
                    }
                }

                Debug.Assert(lastException != null);
                ExceptionDispatchInfo.Throw(lastException);
            }
        }

        /// <summary>
        /// Establishes a connection to a remote host.
        /// </summary>
        /// <param name="host">The hostname of the remote host to connect to.</param>
        /// <param name="port">The port on the remote host to connect to.</param>
        /// <returns>An asynchronous task that completes when the connection is established.</returns>
        public Task ConnectAsync(string host, int port) => ConnectAsync(host, port, default).AsTask();

        /// <summary>
        /// Establishes a connection to a remote host.
        /// </summary>
        /// <param name="host">The hostname of the remote host to connect to.</param>
        /// <param name="port">The port on the remote host to connect to.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
        /// <returns>An asynchronous task that completes when the connection is established.</returns>
        public ValueTask ConnectAsync(string host, int port, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(host);

            EndPoint ep = IPAddress.TryParse(host, out IPAddress? parsedAddress) ? (EndPoint)
                new IPEndPoint(parsedAddress, port) :
                new DnsEndPoint(host, port);
            return ConnectAsync(ep, cancellationToken);
        }

        /// <summary>
        /// Disconnects a connected socket from the remote host.
        /// </summary>
        /// <param name="reuseSocket">Indicates whether the socket should be available for reuse after disconnect.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
        /// <returns>An asynchronous task that completes when the socket is disconnected.</returns>
        public ValueTask DisconnectAsync(bool reuseSocket, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return ValueTask.FromCanceled(cancellationToken);
            }

            AwaitableSocketAsyncEventArgs saea =
                Interlocked.Exchange(ref _singleBufferSendEventArgs, null) ??
                new AwaitableSocketAsyncEventArgs(this, isReceiveForCaching: false);

            saea.DisconnectReuseSocket = reuseSocket;
            saea.WrapExceptionsForNetworkStream = false;

            return saea.DisconnectAsync(this, cancellationToken);
        }

        /// <summary>
        /// Receives data from a connected socket.
        /// </summary>
        /// <param name="buffer">The buffer for the received data.</param>
        /// <returns>An asynchronous task that completes with the number of bytes received.</returns>
        public Task<int> ReceiveAsync(ArraySegment<byte> buffer) =>
            ReceiveAsync(buffer, SocketFlags.None);

        /// <summary>
        /// Receives data from a connected socket.
        /// </summary>
        /// <param name="buffer">The buffer for the received data.</param>
        /// <param name="socketFlags">A bitwise combination of SocketFlags values that will be used when receiving the data.</param>
        /// <returns>An asynchronous task that completes with the number of bytes received.</returns>
        public Task<int> ReceiveAsync(ArraySegment<byte> buffer, SocketFlags socketFlags) => ReceiveAsync(buffer, socketFlags, fromNetworkStream: false);

        internal Task<int> ReceiveAsync(ArraySegment<byte> buffer, SocketFlags socketFlags, bool fromNetworkStream)
        {
            ValidateBuffer(buffer);
            return ReceiveAsync(buffer, socketFlags, fromNetworkStream, default).AsTask();
        }

        /// <summary>
        /// Receives data from a connected socket.
        /// </summary>
        /// <param name="buffer">The buffer for the received data.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
        /// <returns>An asynchronous task that completes with the number of bytes received.</returns>
        public ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ReceiveAsync(buffer, SocketFlags.None, cancellationToken);

        /// <summary>
        /// Receives data from a connected socket.
        /// </summary>
        /// <param name="buffer">The buffer for the received data.</param>
        /// <param name="socketFlags">A bitwise combination of SocketFlags values that will be used when receiving the data.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
        /// <returns>An asynchronous task that completes with the number of bytes received.</returns>
        public ValueTask<int> ReceiveAsync(Memory<byte> buffer, SocketFlags socketFlags, CancellationToken cancellationToken = default) =>
            ReceiveAsync(buffer, socketFlags, fromNetworkStream: false, cancellationToken);

        internal ValueTask<int> ReceiveAsync(Memory<byte> buffer, SocketFlags socketFlags, bool fromNetworkStream, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return ValueTask.FromCanceled<int>(cancellationToken);
            }

            AwaitableSocketAsyncEventArgs saea =
                Interlocked.Exchange(ref _singleBufferReceiveEventArgs, null) ??
                new AwaitableSocketAsyncEventArgs(this, isReceiveForCaching: true);

            Debug.Assert(saea.BufferList == null);
            saea.SetBuffer(buffer);
            saea.SocketFlags = socketFlags;
            saea.WrapExceptionsForNetworkStream = fromNetworkStream;
            return saea.ReceiveAsync(this, cancellationToken);
        }

        /// <summary>
        /// Receives data from a connected socket.
        /// </summary>
        /// <param name="buffers">A list of buffers for the received data.</param>
        /// <returns>An asynchronous task that completes with the number of bytes received.</returns>
        public Task<int> ReceiveAsync(IList<ArraySegment<byte>> buffers) =>
            ReceiveAsync(buffers, SocketFlags.None);

        /// <summary>
        /// Receives data from a connected socket.
        /// </summary>
        /// <param name="buffers">A list of buffers for the received data.</param>
        /// <param name="socketFlags">A bitwise combination of SocketFlags values that will be used when receiving the data.</param>
        /// <returns>An asynchronous task that completes with the number of bytes received.</returns>
        public Task<int> ReceiveAsync(IList<ArraySegment<byte>> buffers, SocketFlags socketFlags)
        {
            ValidateBuffersList(buffers);

            TaskSocketAsyncEventArgs<int>? saea = Interlocked.Exchange(ref _multiBufferReceiveEventArgs, null);
            if (saea is null)
            {
                saea = new TaskSocketAsyncEventArgs<int>();
                saea.Completed += (s, e) => CompleteSendReceive((Socket)s!, (TaskSocketAsyncEventArgs<int>)e, isReceive: true);
            }

            saea.BufferList = buffers;
            saea.SocketFlags = socketFlags;
            return GetTaskForSendReceive(ReceiveAsync(saea), saea, fromNetworkStream: false, isReceive: true);
        }

        /// <summary>
        /// Receives data and returns the endpoint of the sending host.
        /// </summary>
        /// <param name="buffer">The buffer for the received data.</param>
        /// <param name="remoteEndPoint">An endpoint of the same type as the endpoint of the remote host.</param>
        /// <returns>An asynchronous task that completes with a <see cref="SocketReceiveFromResult"/> containing the number of bytes received and the endpoint of the sending host.</returns>
        public Task<SocketReceiveFromResult> ReceiveFromAsync(ArraySegment<byte> buffer, EndPoint remoteEndPoint) =>
            ReceiveFromAsync(buffer, SocketFlags.None, remoteEndPoint);

        /// <summary>
        /// Receives data and returns the endpoint of the sending host.
        /// </summary>
        /// <param name="buffer">The buffer for the received data.</param>
        /// <param name="socketFlags">A bitwise combination of SocketFlags values that will be used when receiving the data.</param>
        /// <param name="remoteEndPoint">An endpoint of the same type as the endpoint of the remote host.</param>
        /// <returns>An asynchronous task that completes with a <see cref="SocketReceiveFromResult"/> containing the number of bytes received and the endpoint of the sending host.</returns>
        public Task<SocketReceiveFromResult> ReceiveFromAsync(ArraySegment<byte> buffer, SocketFlags socketFlags, EndPoint remoteEndPoint)
        {
            ValidateBuffer(buffer);
            return ReceiveFromAsync(buffer, socketFlags, remoteEndPoint, default).AsTask();
        }

        /// <summary>
        /// Receives data and returns the endpoint of the sending host.
        /// </summary>
        /// <param name="buffer">The buffer for the received data.</param>
        /// <param name="remoteEndPoint">An endpoint of the same type as the endpoint of the remote host.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to signal the asynchronous operation should be canceled.</param>
        /// <returns>An asynchronous task that completes with a <see cref="SocketReceiveFromResult"/> containing the number of bytes received and the endpoint of the sending host.</returns>
        public ValueTask<SocketReceiveFromResult> ReceiveFromAsync(Memory<byte> buffer, EndPoint remoteEndPoint, CancellationToken cancellationToken = default) =>
            ReceiveFromAsync(buffer, SocketFlags.None, remoteEndPoint, cancellationToken);

        /// <summary>
        /// Receives data and returns the endpoint of the sending host.
        /// </summary>
        /// <param name="buffer">The buffer for the received data.</param>
        /// <param name="socketFlags">A bitwise combination of SocketFlags values that will be used when receiving the data.</param>
        /// <param name="remoteEndPoint">An endpoint of the same type as the endpoint of the remote host.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to signal the asynchronous operation should be canceled.</param>
        /// <returns>An asynchronous task that completes with a <see cref="SocketReceiveFromResult"/> containing the number of bytes received and the endpoint of the sending host.</returns>
        public ValueTask<SocketReceiveFromResult> ReceiveFromAsync(Memory<byte> buffer, SocketFlags socketFlags, EndPoint remoteEndPoint, CancellationToken cancellationToken = default)
        {
            ValidateReceiveFromEndpointAndState(remoteEndPoint, nameof(remoteEndPoint));

            if (cancellationToken.IsCancellationRequested)
            {
                return ValueTask.FromCanceled<SocketReceiveFromResult>(cancellationToken);
            }

            AwaitableSocketAsyncEventArgs saea =
                Interlocked.Exchange(ref _singleBufferReceiveEventArgs, null) ??
                new AwaitableSocketAsyncEventArgs(this, isReceiveForCaching: true);

            Debug.Assert(saea.BufferList == null);
            saea.SetBuffer(buffer);
            saea.SocketFlags = socketFlags;
            saea.RemoteEndPoint = remoteEndPoint;
            saea._socketAddress = new SocketAddress(AddressFamily);
            if (remoteEndPoint!.AddressFamily != AddressFamily && AddressFamily == AddressFamily.InterNetworkV6 && IsDualMode)
            {
                saea.RemoteEndPoint = s_IPEndPointIPv6;
            }
            saea.WrapExceptionsForNetworkStream = false;
            return saea.ReceiveFromAsync(this, cancellationToken);
        }

        /// <summary>
        /// Receives data and returns the endpoint of the sending host.
        /// </summary>
        /// <param name="buffer">The buffer for the received data.</param>
        /// <param name="socketFlags">A bitwise combination of SocketFlags values that will be used when receiving the data.</param>
        /// <param name="receivedAddress">An <see cref="SocketAddress"/>, that will be updated with value of the remote peer.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to signal the asynchronous operation should be canceled.</param>
        /// <returns>An asynchronous task that completes with a <see cref="SocketReceiveFromResult"/> containing the number of bytes received and the endpoint of the sending host.</returns>
        public ValueTask<int> ReceiveFromAsync(Memory<byte> buffer, SocketFlags socketFlags, SocketAddress receivedAddress, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(receivedAddress);

            if (receivedAddress.Size < SocketAddress.GetMaximumAddressSize(AddressFamily))
            {
                throw new ArgumentOutOfRangeException(nameof(receivedAddress), SR.net_sockets_address_small);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return ValueTask.FromCanceled<int>(cancellationToken);
            }

            AwaitableSocketAsyncEventArgs saea =
                Interlocked.Exchange(ref _singleBufferReceiveEventArgs, null) ??
                new AwaitableSocketAsyncEventArgs(this, isReceiveForCaching: true);

            Debug.Assert(saea.BufferList == null);
            saea.SetBuffer(buffer);
            saea.SocketFlags = socketFlags;
            saea.RemoteEndPoint = null;
            saea._socketAddress = receivedAddress;
            saea.WrapExceptionsForNetworkStream = false;
            return saea.ReceiveFromSocketAddressAsync(this, cancellationToken);
        }

        /// <summary>
        /// Receives data and returns additional information about the sender of the message.
        /// </summary>
        /// <param name="buffer">The buffer for the received data.</param>
        /// <param name="remoteEndPoint">An endpoint of the same type as the endpoint of the remote host.</param>
        /// <returns>An asynchronous task that completes with a <see cref="SocketReceiveMessageFromResult"/> containing the number of bytes received and additional information about the sending host.</returns>
        public Task<SocketReceiveMessageFromResult> ReceiveMessageFromAsync(ArraySegment<byte> buffer, EndPoint remoteEndPoint) =>
            ReceiveMessageFromAsync(buffer, SocketFlags.None, remoteEndPoint);

        /// <summary>
        /// Receives data and returns additional information about the sender of the message.
        /// </summary>
        /// <param name="buffer">The buffer for the received data.</param>
        /// <param name="socketFlags">A bitwise combination of SocketFlags values that will be used when receiving the data.</param>
        /// <param name="remoteEndPoint">An endpoint of the same type as the endpoint of the remote host.</param>
        /// <returns>An asynchronous task that completes with a <see cref="SocketReceiveMessageFromResult"/> containing the number of bytes received and additional information about the sending host.</returns>
        public Task<SocketReceiveMessageFromResult> ReceiveMessageFromAsync(ArraySegment<byte> buffer, SocketFlags socketFlags, EndPoint remoteEndPoint)
        {
            ValidateBuffer(buffer);
            return ReceiveMessageFromAsync(buffer, socketFlags, remoteEndPoint, default).AsTask();
        }

        /// <summary>
        /// Receives data and returns additional information about the sender of the message.
        /// </summary>
        /// <param name="buffer">The buffer for the received data.</param>
        /// <param name="remoteEndPoint">An endpoint of the same type as the endpoint of the remote host.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to signal the asynchronous operation should be canceled.</param>
        /// <returns>An asynchronous task that completes with a <see cref="SocketReceiveMessageFromResult"/> containing the number of bytes received and additional information about the sending host.</returns>
        public ValueTask<SocketReceiveMessageFromResult> ReceiveMessageFromAsync(Memory<byte> buffer, EndPoint remoteEndPoint, CancellationToken cancellationToken = default) =>
            ReceiveMessageFromAsync(buffer, SocketFlags.None, remoteEndPoint, cancellationToken);

        /// <summary>
        /// Receives data and returns additional information about the sender of the message.
        /// </summary>
        /// <param name="buffer">The buffer for the received data.</param>
        /// <param name="socketFlags">A bitwise combination of SocketFlags values that will be used when receiving the data.</param>
        /// <param name="remoteEndPoint">An endpoint of the same type as the endpoint of the remote host.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to signal the asynchronous operation should be canceled.</param>
        /// <returns>An asynchronous task that completes with a <see cref="SocketReceiveMessageFromResult"/> containing the number of bytes received and additional information about the sending host.</returns>
        public ValueTask<SocketReceiveMessageFromResult> ReceiveMessageFromAsync(Memory<byte> buffer, SocketFlags socketFlags, EndPoint remoteEndPoint, CancellationToken cancellationToken = default)
        {
            ValidateReceiveFromEndpointAndState(remoteEndPoint, nameof(remoteEndPoint));
            if (cancellationToken.IsCancellationRequested)
            {
                return ValueTask.FromCanceled<SocketReceiveMessageFromResult>(cancellationToken);
            }

            AwaitableSocketAsyncEventArgs saea =
                Interlocked.Exchange(ref _singleBufferReceiveEventArgs, null) ??
                new AwaitableSocketAsyncEventArgs(this, isReceiveForCaching: true);

            Debug.Assert(saea.BufferList == null);
            saea.SetBuffer(buffer);
            saea.SocketFlags = socketFlags;
            saea.RemoteEndPoint = remoteEndPoint;
            saea.WrapExceptionsForNetworkStream = false;
            return saea.ReceiveMessageFromAsync(this, cancellationToken);
        }

        /// <summary>
        /// Sends data on a connected socket.
        /// </summary>
        /// <param name="buffer">The buffer for the data to send.</param>
        /// <returns>An asynchronous task that completes with the number of bytes sent.</returns>
        public Task<int> SendAsync(ArraySegment<byte> buffer) =>
            SendAsync(buffer, SocketFlags.None);

        /// <summary>
        /// Sends data on a connected socket.
        /// </summary>
        /// <param name="buffer">The buffer for the data to send.</param>
        /// <param name="socketFlags">A bitwise combination of SocketFlags values that will be used when sending the data.</param>
        /// <returns>An asynchronous task that completes with the number of bytes sent.</returns>
        public Task<int> SendAsync(ArraySegment<byte> buffer, SocketFlags socketFlags)
        {
            ValidateBuffer(buffer);
            return SendAsync(buffer, socketFlags, default).AsTask();
        }

        /// <summary>
        /// Sends data on a connected socket.
        /// </summary>
        /// <param name="buffer">The buffer for the data to send.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
        /// <returns>An asynchronous task that completes with the number of bytes sent.</returns>
        public ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            SendAsync(buffer, SocketFlags.None, cancellationToken);

        /// <summary>
        /// Sends data on a connected socket.
        /// </summary>
        /// <param name="buffer">The buffer for the data to send.</param>
        /// <param name="socketFlags">A bitwise combination of SocketFlags values that will be used when sending the data.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
        /// <returns>An asynchronous task that completes with the number of bytes sent.</returns>
        public ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer, SocketFlags socketFlags, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return ValueTask.FromCanceled<int>(cancellationToken);
            }

            AwaitableSocketAsyncEventArgs saea =
                Interlocked.Exchange(ref _singleBufferSendEventArgs, null) ??
                new AwaitableSocketAsyncEventArgs(this, isReceiveForCaching: false);

            Debug.Assert(saea.BufferList == null);
            saea.SetBuffer(MemoryMarshal.AsMemory(buffer));
            saea.SocketFlags = socketFlags;
            saea.WrapExceptionsForNetworkStream = false;
            return saea.SendAsync(this, cancellationToken);
        }

        internal ValueTask SendAsyncForNetworkStream(ReadOnlyMemory<byte> buffer, SocketFlags socketFlags, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return ValueTask.FromCanceled(cancellationToken);
            }

            AwaitableSocketAsyncEventArgs saea =
                Interlocked.Exchange(ref _singleBufferSendEventArgs, null) ??
                new AwaitableSocketAsyncEventArgs(this, isReceiveForCaching: false);

            Debug.Assert(saea.BufferList == null);
            saea.SetBuffer(MemoryMarshal.AsMemory(buffer));
            saea.SocketFlags = socketFlags;
            saea.WrapExceptionsForNetworkStream = true;
            return saea.SendAsyncForNetworkStream(this, cancellationToken);
        }

        /// <summary>
        /// Sends data on a connected socket.
        /// </summary>
        /// <param name="buffers">A list of buffers for the data to send.</param>
        /// <returns>An asynchronous task that completes with the number of bytes sent.</returns>
        public Task<int> SendAsync(IList<ArraySegment<byte>> buffers) =>
            SendAsync(buffers, SocketFlags.None);

        /// <summary>
        /// Sends data on a connected socket.
        /// </summary>
        /// <param name="buffers">A list of buffers for the data to send.</param>
        /// <param name="socketFlags">A bitwise combination of SocketFlags values that will be used when sending the data.</param>
        /// <returns>An asynchronous task that completes with the number of bytes sent.</returns>
        public Task<int> SendAsync(IList<ArraySegment<byte>> buffers, SocketFlags socketFlags)
        {
            ValidateBuffersList(buffers);

            TaskSocketAsyncEventArgs<int>? saea = Interlocked.Exchange(ref _multiBufferSendEventArgs, null);
            if (saea is null)
            {
                saea = new TaskSocketAsyncEventArgs<int>();
                saea.Completed += (s, e) => CompleteSendReceive((Socket)s!, (TaskSocketAsyncEventArgs<int>)e, isReceive: false);
            }

            saea.BufferList = buffers;
            saea.SocketFlags = socketFlags;
            return GetTaskForSendReceive(SendAsync(saea), saea, fromNetworkStream: false, isReceive: false);
        }

        /// <summary>
        /// Sends data to the specified remote host.
        /// </summary>
        /// <param name="buffer">The buffer for the data to send.</param>
        /// <param name="remoteEP">The remote host to which to send the data.</param>
        /// <returns>An asynchronous task that completes with the number of bytes sent.</returns>
        public Task<int> SendToAsync(ArraySegment<byte> buffer, EndPoint remoteEP) =>
            SendToAsync(buffer, SocketFlags.None, remoteEP);

        /// <summary>
        /// Sends data to the specified remote host.
        /// </summary>
        /// <param name="buffer">The buffer for the data to send.</param>
        /// <param name="socketFlags">A bitwise combination of SocketFlags values that will be used when sending the data.</param>
        /// <param name="remoteEP">The remote host to which to send the data.</param>
        /// <returns>An asynchronous task that completes with the number of bytes sent.</returns>
        public Task<int> SendToAsync(ArraySegment<byte> buffer, SocketFlags socketFlags, EndPoint remoteEP)
        {
            ValidateBuffer(buffer);
            return SendToAsync(buffer, socketFlags, remoteEP, default).AsTask();
        }

        /// <summary>
        /// Sends data to the specified remote host.
        /// </summary>
        /// <param name="buffer">The buffer for the data to send.</param>
        /// <param name="remoteEP">The remote host to which to send the data.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
        /// <returns>An asynchronous task that completes with the number of bytes sent.</returns>
        public ValueTask<int> SendToAsync(ReadOnlyMemory<byte> buffer, EndPoint remoteEP, CancellationToken cancellationToken = default) =>
            SendToAsync(buffer, SocketFlags.None, remoteEP, cancellationToken);

        /// <summary>
        /// Sends data to the specified remote host.
        /// </summary>
        /// <param name="buffer">The buffer for the data to send.</param>
        /// <param name="socketFlags">A bitwise combination of SocketFlags values that will be used when sending the data.</param>
        /// <param name="remoteEP">The remote host to which to send the data.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
        /// <returns>An asynchronous task that completes with the number of bytes sent.</returns>
        public ValueTask<int> SendToAsync(ReadOnlyMemory<byte> buffer, SocketFlags socketFlags, EndPoint remoteEP, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(remoteEP);

            if (cancellationToken.IsCancellationRequested)
            {
                return ValueTask.FromCanceled<int>(cancellationToken);
            }

            AwaitableSocketAsyncEventArgs saea =
                Interlocked.Exchange(ref _singleBufferSendEventArgs, null) ??
                new AwaitableSocketAsyncEventArgs(this, isReceiveForCaching: false);

            Debug.Assert(saea.BufferList == null);
            saea.SetBuffer(MemoryMarshal.AsMemory(buffer));
            saea.SocketFlags = socketFlags;
            saea.RemoteEndPoint = remoteEP;
            saea.WrapExceptionsForNetworkStream = false;
            return saea.SendToAsync(this, cancellationToken);
        }

        /// <summary>
        /// Sends data to the specified remote host.
        /// </summary>
        /// <param name="buffer">The buffer for the data to send.</param>
        /// <param name="socketFlags">A bitwise combination of SocketFlags values that will be used when sending the data.</param>
        /// <param name="socketAddress">The remote host to which to send the data.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
        /// <returns>An asynchronous task that completes with the number of bytes sent.</returns>
        public ValueTask<int> SendToAsync(ReadOnlyMemory<byte> buffer, SocketFlags socketFlags, SocketAddress socketAddress, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(socketAddress);

            if (cancellationToken.IsCancellationRequested)
            {
                return ValueTask.FromCanceled<int>(cancellationToken);
            }

            AwaitableSocketAsyncEventArgs saea =
                Interlocked.Exchange(ref _singleBufferSendEventArgs, null) ??
                new AwaitableSocketAsyncEventArgs(this, isReceiveForCaching: false);

            Debug.Assert(saea.BufferList == null);
            saea.SetBuffer(MemoryMarshal.AsMemory(buffer));
            saea.SocketFlags = socketFlags;
            saea._socketAddress = socketAddress;
            saea.RemoteEndPoint = null;
            saea.WrapExceptionsForNetworkStream = false;
            try
            {
                return saea.SendToAsync(this, cancellationToken);
            }
            finally
            {
                // detach user provided SA so we do not accidentally stomp on it later.
                saea._socketAddress = null;
            }
        }

        /// <summary>
        /// Sends the file <paramref name="fileName"/> to a connected <see cref="Socket"/> object.
        /// </summary>
        /// <param name="fileName">A <see cref="string"/> that contains the path and name of the file to be sent. This parameter can be <see langword="null"/>.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
        /// <exception cref="ObjectDisposedException">The <see cref="Socket"/> object has been closed.</exception>
        /// <exception cref="NotSupportedException">The <see cref="Socket"/> object is not connected to a remote host.</exception>
        /// <exception cref="FileNotFoundException">The file <paramref name="fileName"/> was not found.</exception>
        /// <exception cref="SocketException">An error occurred when attempting to access the socket.</exception>
        public ValueTask SendFileAsync(string? fileName, CancellationToken cancellationToken = default)
        {
            return SendFileAsync(fileName, default, default, TransmitFileOptions.UseDefaultWorkerThread, cancellationToken);
        }

        /// <summary>
        /// Sends the file <paramref name="fileName"/> and buffers of data to a connected <see cref="Socket"/> object
        /// using the specified <see cref="TransmitFileOptions"/> value.
        /// </summary>
        /// <param name="fileName">A <see cref="string"/> that contains the path and name of the file to be sent. This parameter can be <see langword="null"/>.</param>
        /// <param name="preBuffer">A <see cref="byte"/> array that contains data to be sent before the file is sent. This parameter can be <see langword="null"/>.</param>
        /// <param name="postBuffer">A <see cref="byte"/> array that contains data to be sent after the file is sent. This parameter can be <see langword="null"/>.</param>
        /// <param name="flags">One or more of <see cref="TransmitFileOptions"/> values.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
        /// <exception cref="ObjectDisposedException">The <see cref="Socket"/> object has been closed.</exception>
        /// <exception cref="NotSupportedException">The <see cref="Socket"/> object is not connected to a remote host.</exception>
        /// <exception cref="FileNotFoundException">The file <paramref name="fileName"/> was not found.</exception>
        /// <exception cref="SocketException">An error occurred when attempting to access the socket.</exception>
        public ValueTask SendFileAsync(string? fileName, ReadOnlyMemory<byte> preBuffer, ReadOnlyMemory<byte> postBuffer, TransmitFileOptions flags, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return ValueTask.FromCanceled(cancellationToken);
            }

            if (!IsConnectionOriented)
            {
                var ex = ExceptionDispatchInfo.SetCurrentStackTrace(new NotSupportedException(SR.net_notconnected));
                return ValueTask.FromException(ex);
            }

            int packetsCount = 0;

            if (fileName is not null)
            {
                packetsCount++;
            }

            if (!preBuffer.IsEmpty)
            {
                packetsCount++;
            }

            if (!postBuffer.IsEmpty)
            {
                packetsCount++;
            }

            AwaitableSocketAsyncEventArgs saea =
                Interlocked.Exchange(ref _singleBufferSendEventArgs, null) ??
                new AwaitableSocketAsyncEventArgs(this, isReceiveForCaching: false);

            SendPacketsElement[] sendPacketsElements = saea.SendPacketsElements?.Length == packetsCount
                ? saea.SendPacketsElements
                : new SendPacketsElement[packetsCount];

            int index = 0;
            if (!preBuffer.IsEmpty)
            {
                sendPacketsElements[index++] = new SendPacketsElement(preBuffer, endOfPacket: index == packetsCount);
            }

            if (fileName is not null)
            {
                sendPacketsElements[index++] = new SendPacketsElement(fileName, 0, 0, endOfPacket: index == packetsCount);
            }

            if (!postBuffer.IsEmpty)
            {
                sendPacketsElements[index++] = new SendPacketsElement(postBuffer, endOfPacket: index == packetsCount);
            }

            Debug.Assert(index == packetsCount);

            saea.SendPacketsFlags = flags;
            saea.SendPacketsElements = sendPacketsElements;
            saea.WrapExceptionsForNetworkStream = false;
            return saea.SendPacketsAsync(this, cancellationToken);
        }

        private static void ValidateBufferArguments(byte[] buffer, int offset, int size)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)offset, (uint)buffer.Length, nameof(offset));
            ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)size, (uint)(buffer.Length - offset), nameof(size));
        }

        /// <summary>Validates the supplied array segment, throwing if its array or indices are null or out-of-bounds, respectively.</summary>
        private static void ValidateBuffer(ArraySegment<byte> buffer)
        {
            ArgumentNullException.ThrowIfNull(buffer.Array, nameof(buffer.Array));
            if ((uint)buffer.Offset > (uint)buffer.Array.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(buffer.Offset));
            }
            if ((uint)buffer.Count > (uint)(buffer.Array.Length - buffer.Offset))
            {
                throw new ArgumentOutOfRangeException(nameof(buffer.Count));
            }
        }

        /// <summary>Validates the supplied buffer list, throwing if it's null or empty.</summary>
        private static void ValidateBuffersList(IList<ArraySegment<byte>> buffers)
        {
            ArgumentNullException.ThrowIfNull(buffers);

            if (buffers.Count == 0)
            {
                throw new ArgumentException(SR.Format(SR.net_sockets_zerolist, nameof(buffers)), nameof(buffers));
            }
        }

        /// <summary>Gets a task to represent the operation.</summary>
        /// <param name="pending">true if the operation completes asynchronously; false if it completed synchronously.</param>
        /// <param name="saea">The event args instance used with the operation.</param>
        /// <param name="fromNetworkStream">
        /// true if the request is coming from NetworkStream, which has special semantics for
        /// exceptions and cached tasks; otherwise, false.
        /// </param>
        /// <param name="isReceive">true if this is a receive; false if this is a send.</param>
        private Task<int> GetTaskForSendReceive(bool pending, TaskSocketAsyncEventArgs<int> saea, bool fromNetworkStream, bool isReceive)
        {
            Task<int> t;

            if (pending)
            {
                // The operation is completing asynchronously (it may have already completed).
                // Get the task for the operation, with appropriate synchronization to coordinate
                // with the async callback that'll be completing the task.
                bool responsibleForReturningToPool;
                t = saea.GetCompletionResponsibility(out responsibleForReturningToPool).Task;
                if (responsibleForReturningToPool)
                {
                    // We're responsible for returning it only if the callback has already been invoked
                    // and gotten what it needs from the SAEA; otherwise, the callback will return it.
                    ReturnSocketAsyncEventArgs(saea, isReceive);
                }
            }
            else
            {
                // The operation completed synchronously.  Get a task for it.
                if (saea.SocketError == SocketError.Success)
                {
                    // Get the number of bytes successfully received/sent.  If the request came from
                    // NetworkStream and this is a send, we can always use 0 (and thus get a cached
                    // task from FromResult), because the caller receives a non-generic Task.
                    t = Task.FromResult(fromNetworkStream & !isReceive ? 0 : saea.BytesTransferred);
                }
                else
                {
                    t = Task.FromException<int>(GetException(saea.SocketError, wrapExceptionsInIOExceptions: fromNetworkStream));
                }

                // There won't be a callback, and we're done with the SAEA, so return it to the pool.
                ReturnSocketAsyncEventArgs(saea, isReceive);
            }

            return t;
        }

        /// <summary>Completes the SocketAsyncEventArg's Task with the result of the send or receive, and returns it to the specified pool.</summary>
        private static void CompleteSendReceive(Socket s, TaskSocketAsyncEventArgs<int> saea, bool isReceive)
        {
            // Pull the relevant state off of the SAEA
            SocketError error = saea.SocketError;
            int bytesTransferred = saea.BytesTransferred;
            bool wrapExceptionsInIOExceptions = saea._wrapExceptionsInIOExceptions;

            // Synchronize with the initiating thread. If the synchronous caller already got what
            // it needs from the SAEA, then we can return it to the pool now. Otherwise, it'll be
            // responsible for returning it once it's gotten what it needs from it.
            bool responsibleForReturningToPool;
            AsyncTaskMethodBuilder<int> builder = saea.GetCompletionResponsibility(out responsibleForReturningToPool);
            if (responsibleForReturningToPool)
            {
                s.ReturnSocketAsyncEventArgs(saea, isReceive);
            }

            // Complete the builder/task with the results.
            if (error == SocketError.Success)
            {
                builder.SetResult(bytesTransferred);
            }
            else
            {
                builder.SetException(GetException(error, wrapExceptionsInIOExceptions));
            }
        }

        /// <summary>Gets a SocketException or an IOException wrapping a SocketException for the specified error.</summary>
        private static Exception GetException(SocketError error, bool wrapExceptionsInIOExceptions = false)
        {
            Exception e = ExceptionDispatchInfo.SetCurrentStackTrace(new SocketException((int)error));
            return wrapExceptionsInIOExceptions ?
                new IOException(SR.Format(SR.net_io_readwritefailure, e.Message), e) :
                e;
        }

        /// <summary>Returns a <see cref="TaskSocketAsyncEventArgs{TResult}"/> instance for reuse.</summary>
        /// <param name="saea">The instance to return.</param>
        /// <param name="isReceive">true if this instance is used for receives; false if used for sends.</param>
        private void ReturnSocketAsyncEventArgs(TaskSocketAsyncEventArgs<int> saea, bool isReceive)
        {
            // Reset state on the SAEA before returning it.  But do not reset buffer state.  That'll be done
            // if necessary by the consumer, but we want to keep the buffers due to likely subsequent reuse
            // and the costs associated with changing them.
            saea._accessed = false;
            saea._builder = default;
            saea._wrapExceptionsInIOExceptions = false;

            // Write this instance back as a cached instance, only if there isn't currently one cached.
            ref TaskSocketAsyncEventArgs<int>? cache = ref isReceive ? ref _multiBufferReceiveEventArgs : ref _multiBufferSendEventArgs;
            if (Interlocked.CompareExchange(ref cache, saea, null) != null)
            {
                saea.Dispose();
            }
        }

        /// <summary>Dispose of any cached <see cref="TaskSocketAsyncEventArgs{TResult}"/> instances.</summary>
        private void DisposeCachedTaskSocketAsyncEventArgs()
        {
            Interlocked.Exchange(ref _multiBufferReceiveEventArgs, null)?.Dispose();
            Interlocked.Exchange(ref _multiBufferSendEventArgs, null)?.Dispose();
            Interlocked.Exchange(ref _singleBufferReceiveEventArgs, null)?.Dispose();
            Interlocked.Exchange(ref _singleBufferSendEventArgs, null)?.Dispose();
        }

        /// <summary>A SocketAsyncEventArgs with an associated async method builder.</summary>
        private sealed class TaskSocketAsyncEventArgs<TResult> : SocketAsyncEventArgs
        {
            /// <summary>
            /// The builder used to create the Task representing the result of the async operation.
            /// This is a mutable struct.
            /// </summary>
            internal AsyncTaskMethodBuilder<TResult> _builder;
            /// <summary>
            /// Whether the instance was already accessed as part of the operation.  We expect
            /// at most two accesses: one from the synchronous caller to initiate the operation,
            /// and one from the callback if the operation completes asynchronously.  If it completes
            /// synchronously, then it's the initiator's responsbility to return the instance to
            /// the pool.  If it completes asynchronously, then it's the responsibility of whoever
            /// accesses this second, so we track whether it's already been accessed.
            /// </summary>
            internal bool _accessed;
            /// <summary>Whether exceptions that emerge should be wrapped in IOExceptions.</summary>
            internal bool _wrapExceptionsInIOExceptions;

            internal TaskSocketAsyncEventArgs() :
                base(unsafeSuppressExecutionContextFlow: true) // avoid flowing context at lower layers as we only expose Task, which handles it
            {
            }

            /// <summary>Gets the builder's task with appropriate synchronization.</summary>
            internal AsyncTaskMethodBuilder<TResult> GetCompletionResponsibility(out bool responsibleForReturningToPool)
            {
                lock (this)
                {
                    responsibleForReturningToPool = _accessed;
                    _accessed = true;
                    _ = _builder.Task; // force initialization under the lock (builder itself lazily initializes w/o synchronization)
                    return _builder;
                }
            }
        }

        /// <summary>A SocketAsyncEventArgs that can be awaited to get the result of an operation.</summary>
        internal sealed class AwaitableSocketAsyncEventArgs : SocketAsyncEventArgs, IValueTaskSource, IValueTaskSource<int>, IValueTaskSource<Socket>, IValueTaskSource<SocketReceiveFromResult>, IValueTaskSource<SocketReceiveMessageFromResult>
        {
            /// <summary>The owning socket.</summary>
            private readonly Socket _owner;
            /// <summary>Whether this should be cached as a read or a write on the <see cref="_owner"/></summary>
            private readonly bool _isReadForCaching;
            /// <summary>Core logic for the IValueTaskSource implementations.</summary>
            private ManualResetValueTaskSourceCore<bool> _mrvtsc;
            /// <summary>The cancellation token used for the current operation. Stored to propagate the most relevant exception.</summary>
            private CancellationToken _cancellationToken;

            /// <summary>Initializes the event args.</summary>
            public AwaitableSocketAsyncEventArgs(Socket owner, bool isReceiveForCaching) :
                base(unsafeSuppressExecutionContextFlow: true) // avoid flowing context at lower layers as we only expose ValueTask, which handles it
            {
                _owner = owner;
                _isReadForCaching = isReceiveForCaching;
            }

            public bool WrapExceptionsForNetworkStream { get; set; }

            /// <summary>Resets this instance after an asynchronous completion and puts it back into the pool.</summary>
            private void ReleaseForAsyncCompletion()
            {
                _cancellationToken = default;
                _mrvtsc.Reset();
                ReleaseForSyncCompletion();
            }

            /// <summary>Resets this instance after a synchronous completion and puts it back into the pool.</summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void ReleaseForSyncCompletion()
            {
                ref AwaitableSocketAsyncEventArgs? cache = ref _isReadForCaching ? ref _owner._singleBufferReceiveEventArgs : ref _owner._singleBufferSendEventArgs;
                if (Interlocked.CompareExchange(ref cache, this, null) != null)
                {
                    Dispose();
                }
            }

            protected override void OnCompleted(SocketAsyncEventArgs _) => _mrvtsc.SetResult(true);

            /// <summary>Initiates an accept operation on the associated socket.</summary>
            /// <returns>This instance.</returns>
            public ValueTask<Socket> AcceptAsync(Socket socket, CancellationToken cancellationToken)
            {
                if (socket.AcceptAsync(this, cancellationToken))
                {
                    _cancellationToken = cancellationToken;
                    return new ValueTask<Socket>(this, _mrvtsc.Version);
                }

                Socket acceptSocket = AcceptSocket!;
                SocketError error = SocketError;

                AcceptSocket = null;

                ReleaseForSyncCompletion();

                return error == SocketError.Success ?
                    new ValueTask<Socket>(acceptSocket) :
                    ValueTask.FromException<Socket>(CreateException(error));
            }

            /// <summary>Initiates a receive operation on the associated socket.</summary>
            /// <returns>This instance.</returns>
            public ValueTask<int> ReceiveAsync(Socket socket, CancellationToken cancellationToken)
            {
                if (socket.ReceiveAsync(this, cancellationToken))
                {
                    _cancellationToken = cancellationToken;
                    return new ValueTask<int>(this, _mrvtsc.Version);
                }

                int bytesTransferred = BytesTransferred;
                SocketError error = SocketError;

                ReleaseForSyncCompletion();

                return error == SocketError.Success ?
                    new ValueTask<int>(bytesTransferred) :
                    ValueTask.FromException<int>(CreateException(error));
            }

            public ValueTask<SocketReceiveFromResult> ReceiveFromAsync(Socket socket, CancellationToken cancellationToken)
            {
                if (socket.ReceiveFromAsync(this, cancellationToken))
                {
                    _cancellationToken = cancellationToken;
                    return new ValueTask<SocketReceiveFromResult>(this, _mrvtsc.Version);
                }

                int bytesTransferred = BytesTransferred;
                EndPoint remoteEndPoint = RemoteEndPoint!;
                SocketError error = SocketError;

                ReleaseForSyncCompletion();

                return error == SocketError.Success ?
                    new ValueTask<SocketReceiveFromResult>(new SocketReceiveFromResult() { ReceivedBytes = bytesTransferred, RemoteEndPoint = remoteEndPoint }) :
                    ValueTask.FromException<SocketReceiveFromResult>(CreateException(error));
            }

            internal ValueTask<int> ReceiveFromSocketAddressAsync(Socket socket, CancellationToken cancellationToken)
            {
                if (socket.ReceiveFromAsync(this, cancellationToken))
                {
                    _cancellationToken = cancellationToken;
                    return new ValueTask<int>(this, _mrvtsc.Version);
                }

                int bytesTransferred = BytesTransferred;
                SocketError error = SocketError;

                ReleaseForSyncCompletion();

                return error == SocketError.Success ?
                    new ValueTask<int>(bytesTransferred) :
                    ValueTask.FromException<int>(CreateException(error));
            }

            public ValueTask<SocketReceiveMessageFromResult> ReceiveMessageFromAsync(Socket socket, CancellationToken cancellationToken)
            {
                if (socket.ReceiveMessageFromAsync(this, cancellationToken))
                {
                    _cancellationToken = cancellationToken;
                    return new ValueTask<SocketReceiveMessageFromResult>(this, _mrvtsc.Version);
                }

                int bytesTransferred = BytesTransferred;
                EndPoint remoteEndPoint = RemoteEndPoint!;
                SocketFlags socketFlags = SocketFlags;
                IPPacketInformation packetInformation = ReceiveMessageFromPacketInfo;
                SocketError error = SocketError;

                ReleaseForSyncCompletion();

                return error == SocketError.Success ?
                    new ValueTask<SocketReceiveMessageFromResult>(new SocketReceiveMessageFromResult() { ReceivedBytes = bytesTransferred, RemoteEndPoint = remoteEndPoint, SocketFlags = socketFlags, PacketInformation = packetInformation }) :
                    ValueTask.FromException<SocketReceiveMessageFromResult>(CreateException(error));
            }

            /// <summary>Initiates a send operation on the associated socket.</summary>
            /// <returns>This instance.</returns>
            public ValueTask<int> SendAsync(Socket socket, CancellationToken cancellationToken)
            {
                if (socket.SendAsync(this, cancellationToken))
                {
                    _cancellationToken = cancellationToken;
                    return new ValueTask<int>(this, _mrvtsc.Version);
                }

                int bytesTransferred = BytesTransferred;
                SocketError error = SocketError;

                ReleaseForSyncCompletion();

                return error == SocketError.Success ?
                    new ValueTask<int>(bytesTransferred) :
                    ValueTask.FromException<int>(CreateException(error));
            }

            public ValueTask SendAsyncForNetworkStream(Socket socket, CancellationToken cancellationToken)
            {
                if (socket.SendAsync(this, cancellationToken))
                {
                    _cancellationToken = cancellationToken;
                    return new ValueTask(this, _mrvtsc.Version);
                }

                SocketError error = SocketError;

                ReleaseForSyncCompletion();

                return error == SocketError.Success ?
                    default :
                    ValueTask.FromException(CreateException(error));
            }

            public ValueTask SendPacketsAsync(Socket socket, CancellationToken cancellationToken)
            {
                if (socket.SendPacketsAsync(this, cancellationToken))
                {
                    _cancellationToken = cancellationToken;
                    return new ValueTask(this, _mrvtsc.Version);
                }

                SocketError error = SocketError;

                ReleaseForSyncCompletion();

                return error == SocketError.Success ?
                    default :
                    ValueTask.FromException(CreateException(error));
            }

            public ValueTask<int> SendToAsync(Socket socket, CancellationToken cancellationToken)
            {
                if (socket.SendToAsync(this, cancellationToken))
                {
                    _cancellationToken = cancellationToken;
                    return new ValueTask<int>(this, _mrvtsc.Version);
                }

                int bytesTransferred = BytesTransferred;
                SocketError error = SocketError;

                ReleaseForSyncCompletion();

                return error == SocketError.Success ?
                    new ValueTask<int>(bytesTransferred) :
                    ValueTask.FromException<int>(CreateException(error));
            }

            public ValueTask ConnectAsync(Socket socket, CancellationToken cancellationToken)
            {
                try
                {
                    if (socket.ConnectAsync(this, userSocket: true, saeaMultiConnectCancelable: false, cancellationToken))
                    {
                        _cancellationToken = cancellationToken;
                        return new ValueTask(this, _mrvtsc.Version);
                    }
                }
                catch
                {
                    ReleaseForSyncCompletion();
                    throw;
                }

                SocketError error = SocketError;

                ReleaseForSyncCompletion();

                return error == SocketError.Success ?
                    default :
                    ValueTask.FromException(CreateException(error));
            }

            public ValueTask DisconnectAsync(Socket socket, CancellationToken cancellationToken)
            {
                if (socket.DisconnectAsync(this, cancellationToken))
                {
                    _cancellationToken = cancellationToken;
                    return new ValueTask(this, _mrvtsc.Version);
                }

                SocketError error = SocketError;

                ReleaseForSyncCompletion();

                return error == SocketError.Success ?
                    ValueTask.CompletedTask :
                    ValueTask.FromException(CreateException(error));
            }

            /// <summary>Gets the status of the operation.</summary>
            public ValueTaskSourceStatus GetStatus(short token) =>
                _mrvtsc.GetStatus(token);

            /// <summary>Queues the provided continuation to be executed once the operation has completed.</summary>
            public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) =>
                _mrvtsc.OnCompleted(continuation, state, token, flags);

            /// <summary>Gets the result of the completion operation.</summary>
            /// <returns>Number of bytes transferred.</returns>
            /// <remarks>
            /// Unlike TaskAwaiter's GetResult, this does not block until the operation completes: it must only
            /// be used once the operation has completed.  This is handled implicitly by await.
            /// </remarks>
            int IValueTaskSource<int>.GetResult(short token)
            {
                if (token != _mrvtsc.Version)
                {
                    ThrowIncorrectTokenException();
                }

                SocketError error = SocketError;
                int bytes = BytesTransferred;
                CancellationToken cancellationToken = _cancellationToken;

                ReleaseForAsyncCompletion();

                if (error != SocketError.Success)
                {
                    ThrowException(error, cancellationToken);
                }
                return bytes;
            }

            void IValueTaskSource.GetResult(short token)
            {
                if (token != _mrvtsc.Version)
                {
                    ThrowIncorrectTokenException();
                }

                SocketError error = SocketError;
                CancellationToken cancellationToken = _cancellationToken;

                ReleaseForAsyncCompletion();

                if (error != SocketError.Success)
                {
                    ThrowException(error, cancellationToken);
                }
            }

            Socket IValueTaskSource<Socket>.GetResult(short token)
            {
                if (token != _mrvtsc.Version)
                {
                    ThrowIncorrectTokenException();
                }

                SocketError error = SocketError;
                Socket acceptSocket = AcceptSocket!;
                CancellationToken cancellationToken = _cancellationToken;

                AcceptSocket = null;

                ReleaseForAsyncCompletion();

                if (error != SocketError.Success)
                {
                    ThrowException(error, cancellationToken);
                }
                return acceptSocket;
            }

            SocketReceiveFromResult IValueTaskSource<SocketReceiveFromResult>.GetResult(short token)
            {
                if (token != _mrvtsc.Version)
                {
                    ThrowIncorrectTokenException();
                }

                SocketError error = SocketError;
                int bytes = BytesTransferred;
                EndPoint remoteEndPoint = RemoteEndPoint!;
                CancellationToken cancellationToken = _cancellationToken;

                ReleaseForAsyncCompletion();

                if (error != SocketError.Success)
                {
                    ThrowException(error, cancellationToken);
                }

                return new SocketReceiveFromResult() { ReceivedBytes = bytes, RemoteEndPoint = remoteEndPoint };
            }

            SocketReceiveMessageFromResult IValueTaskSource<SocketReceiveMessageFromResult>.GetResult(short token)
            {
                if (token != _mrvtsc.Version)
                {
                    ThrowIncorrectTokenException();
                }

                SocketError error = SocketError;
                int bytes = BytesTransferred;
                EndPoint remoteEndPoint = RemoteEndPoint!;
                SocketFlags socketFlags = SocketFlags;
                IPPacketInformation packetInformation = ReceiveMessageFromPacketInfo;
                CancellationToken cancellationToken = _cancellationToken;

                ReleaseForAsyncCompletion();

                if (error != SocketError.Success)
                {
                    ThrowException(error, cancellationToken);
                }

                return new SocketReceiveMessageFromResult() { ReceivedBytes = bytes, RemoteEndPoint = remoteEndPoint, SocketFlags = socketFlags, PacketInformation = packetInformation };
            }

            private static void ThrowIncorrectTokenException() => throw new InvalidOperationException(SR.InvalidOperation_IncorrectToken);

            private void ThrowException(SocketError error, CancellationToken cancellationToken)
            {
                // Most operations will report OperationAborted when canceled.
                // On Windows, SendFileAsync will report ConnectionAborted.
                // There's a race here anyway, so there's no harm in also checking for ConnectionAborted in all cases.
                if (error is SocketError.OperationAborted or SocketError.ConnectionAborted)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                throw CreateException(error, forAsyncThrow: false);
            }

            private Exception CreateException(SocketError error, bool forAsyncThrow = true)
            {
                Exception e = new SocketException((int)error);

                if (forAsyncThrow)
                {
                    e = ExceptionDispatchInfo.SetCurrentStackTrace(e);
                }

                return WrapExceptionsForNetworkStream ?
                    new IOException(SR.Format(_isReadForCaching ? SR.net_io_readfailure : SR.net_io_writefailure, e.Message), e) :
                    e;
            }
        }
    }
}
