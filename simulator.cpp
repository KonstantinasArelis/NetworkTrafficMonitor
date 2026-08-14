#include <iostream>
#include <sys/socket.h> // creating sockets
#include <netinet/in.h> // data structures for interent address
#include <arpa/inet.h> // convert ip address into binary format
#include <unistd.h> // posix system api

int main() 
{
    std::cout << "Hello World! - says traffic simulator" << std::endl;

    // create socket
    int sockfd = socket(AF_INET, SOCK_DGRAM, 0);
    if (sockfd < 0)
    {
        std::cout << "Failed to open socket, return value: " << sockfd << std::endl;
        return 1;
    }

    sockaddr_in socketStruct{};
    socketStruct.sin_family = AF_INET;
    socketStruct.sin_port = htons(9999);
    inet_pton(AF_INET, "127.0.0.1", &socketStruct.sin_addr);

    char myMessage[] = "something";

    while(true)
    {
        sendto(sockfd, myMessage, sizeof(myMessage), 0, (struct sockaddr*)&socketStruct, sizeof(socketStruct));
    }

    return 0;
}