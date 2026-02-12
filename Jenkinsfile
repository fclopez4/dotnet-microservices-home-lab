pipeline {
    agent any

    environment {
        HARBOR_REGISTRY = 'server:8085'
        HARBOR_PROJECT  = 'project'
        SONAR_HOST      = 'http://server:9000'
        IMAGE_TAG       = "${env.BUILD_NUMBER}"
        DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    }

    stages {
        stage('Checkout') {
            steps {
                checkout scm
            }
        }

        stage('Restore & Build') {
            steps {
                sh '''
                    dotnet restore
                    dotnet build --configuration Release --no-restore
                '''
            }
        }

        stage('Test') {
            steps {
                sh '''
                    dotnet test --configuration Release --no-build \
                        --logger "trx;LogFileName=results.trx" \
                        --collect:"XPlat Code Coverage" \
                        -- DataCollectors.DataCollector.Configuration.Format=opencover
                '''
            }
        }

        stage('SonarQube Analysis') {
            steps {
                withCredentials([string(credentialsId: 'sonar-token', variable: 'SONAR_TOKEN')]) {
                    sh '''
                        export PATH="$PATH:/root/.dotnet/tools:/usr/local/bin"

                        dotnet sonarscanner begin \
                            /k:"project" \
                            /d:sonar.host.url="${SONAR_HOST}" \
                            /d:sonar.token="${SONAR_TOKEN}" \
                            /d:sonar.cs.opencover.reportsPaths="**/coverage.opencover.xml"

                        dotnet build --configuration Release --no-restore

                        dotnet sonarscanner end /d:sonar.token="${SONAR_TOKEN}"
                    '''
                }
            }
        }

        stage('Docker Build') {
            steps {
                sh """
                    docker build -t ${HARBOR_REGISTRY}/${HARBOR_PROJECT}/api:${IMAGE_TAG} \
                        -f src/Project.Api/Dockerfile .

                    docker build -t ${HARBOR_REGISTRY}/${HARBOR_PROJECT}/worker:${IMAGE_TAG} \
                        -f src/Project.Worker/Dockerfile .

                    docker build -t ${HARBOR_REGISTRY}/${HARBOR_PROJECT}/frontend:${IMAGE_TAG} \
                        -f frontend/Dockerfile frontend/
                """
            }
        }

        stage('Trivy Security Scan') {
            steps {
                sh """
                    echo "=== Scanning API image ==="
                    trivy image --exit-code 0 --severity CRITICAL,HIGH \
                        --format table \
                        ${HARBOR_REGISTRY}/${HARBOR_PROJECT}/api:${IMAGE_TAG}

                    echo "=== Scanning Worker image ==="
                    trivy image --exit-code 0 --severity CRITICAL,HIGH \
                        --format table \
                        ${HARBOR_REGISTRY}/${HARBOR_PROJECT}/worker:${IMAGE_TAG}

                    echo "=== Scanning Frontend image ==="
                    trivy image --exit-code 0 --severity CRITICAL,HIGH \
                        --format table \
                        ${HARBOR_REGISTRY}/${HARBOR_PROJECT}/frontend:${IMAGE_TAG}
                """
            }
        }

        stage('Push to Harbor') {
            steps {
                withCredentials([usernamePassword(
                    credentialsId: 'harbor-creds',
                    usernameVariable: 'HARBOR_USER',
                    passwordVariable: 'HARBOR_PASS')]) {
                    sh """
                        echo "\${HARBOR_PASS}" | docker login ${HARBOR_REGISTRY} -u \${HARBOR_USER} --password-stdin

                        docker push ${HARBOR_REGISTRY}/${HARBOR_PROJECT}/api:${IMAGE_TAG}
                        docker push ${HARBOR_REGISTRY}/${HARBOR_PROJECT}/worker:${IMAGE_TAG}
                        docker push ${HARBOR_REGISTRY}/${HARBOR_PROJECT}/frontend:${IMAGE_TAG}

                        docker tag ${HARBOR_REGISTRY}/${HARBOR_PROJECT}/api:${IMAGE_TAG} \
                            ${HARBOR_REGISTRY}/${HARBOR_PROJECT}/api:latest
                        docker tag ${HARBOR_REGISTRY}/${HARBOR_PROJECT}/worker:${IMAGE_TAG} \
                            ${HARBOR_REGISTRY}/${HARBOR_PROJECT}/worker:latest
                        docker tag ${HARBOR_REGISTRY}/${HARBOR_PROJECT}/frontend:${IMAGE_TAG} \
                            ${HARBOR_REGISTRY}/${HARBOR_PROJECT}/frontend:latest

                        docker push ${HARBOR_REGISTRY}/${HARBOR_PROJECT}/api:latest
                        docker push ${HARBOR_REGISTRY}/${HARBOR_PROJECT}/worker:latest
                        docker push ${HARBOR_REGISTRY}/${HARBOR_PROJECT}/frontend:latest
                    """
                }
            }
        }

        stage('Deploy to K3s') {
            steps {
                sh """
                    export KUBECONFIG=/var/lib/jenkins/.kube/config

                    kubectl apply -R -f k8s/

                    kubectl set image deployment/api \
                        api=${HARBOR_REGISTRY}/${HARBOR_PROJECT}/api:${IMAGE_TAG} \
                        -n project

                    kubectl set image deployment/worker \
                        worker=${HARBOR_REGISTRY}/${HARBOR_PROJECT}/worker:${IMAGE_TAG} \
                        -n project

                    kubectl set image deployment/frontend \
                        frontend=${HARBOR_REGISTRY}/${HARBOR_PROJECT}/frontend:${IMAGE_TAG} \
                        -n project

                    kubectl rollout status deployment/api -n project --timeout=120s
                    kubectl rollout status deployment/worker -n project --timeout=120s
                    kubectl rollout status deployment/frontend -n project --timeout=60s
                """
            }
        }

        stage('Verify Deployment') {
            steps {
                sh '''
                    export KUBECONFIG=/var/lib/jenkins/.kube/config
                    API_IP=$(kubectl get nodes -o jsonpath='{.items[0].status.addresses[0].address}')

                    echo "=== Verifying API health ==="
                    HEALTHY=false
                    for i in 1 2 3 4 5; do
                        HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" http://${API_IP}:30080/health || echo "000")
                        if [ "$HTTP_CODE" = "200" ]; then
                            echo "API health check PASSED (HTTP 200)"
                            HEALTHY=true
                            break
                        fi
                        echo "Attempt $i: HTTP $HTTP_CODE, retrying in 10s..."
                        sleep 10
                    done

                    if [ "$HEALTHY" != "true" ]; then
                        echo "API health check FAILED after 5 attempts"
                        exit 1
                    fi

                    echo "=== Verifying Frontend ==="
                    HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" http://${API_IP}:30081/ || echo "000")
                    echo "Frontend: HTTP $HTTP_CODE"
                    if [ "$HTTP_CODE" != "200" ]; then
                        echo "Frontend health check FAILED"
                        exit 1
                    fi

                    echo "=== Verifying Grafana ==="
                    HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" http://${API_IP}:30030/api/health || echo "000")
                    echo "Grafana: HTTP $HTTP_CODE"
                    if [ "$HTTP_CODE" != "200" ]; then
                        echo "WARNING: Grafana health check returned HTTP $HTTP_CODE (non-blocking)"
                    fi

                    echo "=== Verifying Worker ==="
                    WORKER_READY=$(kubectl get deployment worker -n project -o jsonpath='{.status.readyReplicas}' 2>/dev/null || echo "0")
                    echo "Worker ready replicas: $WORKER_READY"
                    if [ "$WORKER_READY" -lt "1" ]; then
                        echo "Worker health check FAILED"
                        exit 1
                    fi

                    echo "=== All deployments verified ==="
                '''
            }
        }
    }

    post {
        always {
            sh """
                docker rmi ${HARBOR_REGISTRY}/${HARBOR_PROJECT}/api:${IMAGE_TAG} || true
                docker rmi ${HARBOR_REGISTRY}/${HARBOR_PROJECT}/worker:${IMAGE_TAG} || true
                docker rmi ${HARBOR_REGISTRY}/${HARBOR_PROJECT}/frontend:${IMAGE_TAG} || true
            """
        }
        success { echo 'Pipeline completed OK' }
        failure { echo 'Pipeline FAILED' }
    }
}
